using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using OrchardCore.Modules;
using StreamWriter = System.IO.StreamWriter;

namespace OrchardCore.FileStorage.Minio;

public class MinioFileStore : IFileStore
{
    private readonly IClock _clock;
    private readonly MinioStorageOptions _options;
    private readonly IMinioClient _minioClient;

    public MinioFileStore(IClock clock, MinioStorageOptions options, IMinioClient minioClient)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _minioClient = minioClient ?? throw new ArgumentNullException(nameof(minioClient));
        
        if (string.IsNullOrWhiteSpace(_options.BucketName))
        {
            throw new ArgumentException("The bucket name is required.", nameof(_options.BucketName));
        }
        
        if (string.IsNullOrWhiteSpace(_options.AccessKey))
        {
            throw new ArgumentException("The access key is required.", nameof(_options.AccessKey));
        }
    }
    
    public async Task<IFileStoreEntry> GetFileInfoAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(path);

            var fileInfo = await _minioClient.StatObjectAsync(statObjectArgs);

            return new MinioFile(
                path,
                fileInfo.Size,
                fileInfo.LastModified
            );
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to get file info for {path}: {ex.Message}", ex);
        }
    }

    public async Task<IFileStoreEntry> GetDirectoryInfoAsync(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                return new MinioDirectory(path, _clock.UtcNow);
            }
        
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(path);
            var statObject = await _minioClient.StatObjectAsync(statObjectArgs);
            return new MinioDirectory(path, statObject.LastModified);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to get directory info for {path}: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<IFileStoreEntry> GetDirectoryContentAsync(string? path = null, bool includeSubDirectories = false)
    {
        if (string.IsNullOrEmpty(path))
        {
            yield return new MinioDirectory(string.Empty, _clock.UtcNow);
        }
        
        var listObjectsArgs = new ListObjectsArgs()
            .WithBucket(_options.BucketName)
            .WithPrefix(path)
            .WithRecursive(includeSubDirectories);

        IAsyncEnumerable<Item> items;
        try
        {
            items = _minioClient.ListObjectsEnumAsync(listObjectsArgs);
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to list directory contents for {path}: {ex.Message}", ex);
        }

        await foreach (var file in items)
        {
            if (file.IsDir)
            {
                yield return new MinioDirectory(file.Key, Convert.ToDateTime(file.LastModified));
            }
            else
            {
                yield return new MinioFile(file.Key, (long)file.Size, Convert.ToDateTime(file.LastModified));
            }
        }
    }

    public async Task<bool> TryCreateDirectoryAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        // Minio does not support creating directories, so we create an empty object with the directory name
        var pathWithTrailingSlash = path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
        var tempFile = pathWithTrailingSlash + ".directory";
        
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream);
        writer.Write(string.Empty); // Create an empty file
        await writer.FlushAsync();
        stream.Position = 0;

        try
        {
            await CreateFileFromStreamAsync(tempFile, stream);
            return true;
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to create directory {path}: {ex.Message}", ex);
        }
    }

    public async Task<bool> TryDeleteFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        try
        {
            await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(path));
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to delete file {path}: {ex.Message}", ex);
        }
    }

    public async Task<bool> TryDeleteDirectoryAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        try
        {
            var listObjectsArgs = new ListObjectsArgs()
                .WithBucket(_options.BucketName)
                .WithPrefix(path);

            await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs))
            {
                await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                    .WithBucket(_options.BucketName)
                    .WithObject(item.Key));
            }
            return true;
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to delete directory {path}: {ex.Message}", ex);
        }
    }

    public async Task MoveFileAsync(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath))
        {
            throw new ArgumentException("Old path cannot be null or empty.", nameof(oldPath));
        }
        if (string.IsNullOrWhiteSpace(newPath))
        {
            throw new ArgumentException("New path cannot be null or empty.", nameof(newPath));
        }

        try
        {
            await CopyFileAsync(oldPath, newPath);
            await TryDeleteFileAsync(oldPath);
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to move file from {oldPath} to {newPath}: {ex.Message}", ex);
        }
    }

    public async Task CopyFileAsync(string srcPath, string dstPath)
    {
        if (string.IsNullOrWhiteSpace(srcPath))
        {
            throw new ArgumentException("Source path cannot be null or empty.", nameof(srcPath));
        }
        if (string.IsNullOrWhiteSpace(dstPath))
        {
            throw new ArgumentException("Destination path cannot be null or empty.", nameof(dstPath));
        }

        try
        {
            await _minioClient.CopyObjectAsync(new CopyObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(dstPath)
                .WithCopyObjectSource(new CopySourceObjectArgs()
                    .WithBucket(_options.BucketName)
                    .WithObject(srcPath)));
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to copy file from {srcPath} to {dstPath}: {ex.Message}", ex);
        }
    }

    public async Task<Stream> GetFileStreamAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        var memoryStream = new MemoryStream();
        try
        {
            await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(path)
                .WithCallbackStream((stream) =>
                {
                    stream.CopyTo(memoryStream);
                }));

            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        }
        catch (MinioException ex)
        {
            memoryStream.Dispose();
            throw new FileStoreException($"Failed to get file stream for {path}: {ex.Message}", ex);
        }
    }

    public async Task<Stream> GetFileStreamAsync(IFileStoreEntry fileStoreEntry)
    {
        if (fileStoreEntry == null)
        {
            throw new ArgumentNullException(nameof(fileStoreEntry));
        }
        return await GetFileStreamAsync(fileStoreEntry.Path);
    }

    public async Task<string> CreateFileFromStreamAsync(string path, Stream inputStream, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }
        if (inputStream == null)
        {
            throw new ArgumentNullException(nameof(inputStream));
        }

        try
        {
            if (!overwrite)
            {
                var existingFile = await GetFileInfoAsync(path);
                if (existingFile != null)
                {
                    throw new FileStoreException($"File {path} already exists and overwrite is not allowed.");
                }
            }

            var response = await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(path)
                .WithStreamData(inputStream)
                .WithObjectSize(inputStream.Length));
            
            if (response.Size != inputStream.Length)
            {
                throw new FileStoreException($"Failed to create file {path}: Uploaded size ({response.Size}) does not match input size ({inputStream.Length})");
            }

            return path;
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to create file {path}: {ex.Message}", ex);
        }
    }
}