using Minio;
using Minio.ApiEndpoints;
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
        _clock = clock;
        _options = options;
        _minioClient = minioClient;
        
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
        catch (MinioException)
        {
            return null;
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
        catch (MinioException)
        {
            return null;
        }
    }

    public async IAsyncEnumerable<IFileStoreEntry> GetDirectoryContentAsync(string path = null, bool includeSubDirectories = false)
    {
        if (string.IsNullOrEmpty(path))
        {
            yield return new MinioDirectory(path, _clock.UtcNow);
        }
        
        var listObjectsArgs = new ListObjectsArgs()
            .WithBucket(_options.BucketName)
            .WithPrefix(path)
            .WithRecursive(includeSubDirectories);


        await foreach (var file in _minioClient.ListObjectsEnumAsync(listObjectsArgs))
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
        // Minio does not support creating directories, so we create an empty object with the directory name.
        var pathWithTrailingSlash = path.EndsWith($"/") ? path : path + "/";
        var tempFile = pathWithTrailingSlash + "temp_file";
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write("This is temporary file to create a directory");
        writer.Flush();
        stream.Position = 0;

        try
        {
            await CreateFileFromStreamAsync(tempFile, stream);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            stream.Dispose();
            writer.Dispose();
        }
    }

    public async Task<bool> TryDeleteFileAsync(string path)
    {
        try
        {
            await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(path));
            return true;
        }
        catch (MinioException)
        {
            return false;
        }
    }

    public async Task<bool> TryDeleteDirectoryAsync(string path)
    {
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(_options.BucketName)
            .WithObject(path);
        await _minioClient.RemoveObjectAsync(removeObjectArgs);
        return true;
    }

    public async Task MoveFileAsync(string oldPath, string newPath)
    {
        await CopyFileAsync(oldPath, newPath);
        await TryDeleteFileAsync(oldPath);
    }

    public async Task CopyFileAsync(string srcPath, string dstPath)
    {
        await _minioClient.CopyObjectAsync(new CopyObjectArgs()
            .WithBucket(_options.BucketName)
            .WithObject(dstPath)
            .WithCopyObjectSource(new CopySourceObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(srcPath))
            );
    }

    public async Task<Stream> GetFileStreamAsync(string path)
    {
        var memoryStream = new MemoryStream();
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

    public async Task<Stream> GetFileStreamAsync(IFileStoreEntry fileStoreEntry)
    {
        return await GetFileStreamAsync(fileStoreEntry.Path);
    }

    public async Task<string> CreateFileFromStreamAsync(string path, Stream inputStream, bool overwrite = false)
    {
        try
        {
            if (!overwrite)
            {
                var existingFile = await GetFileInfoAsync(path);
                if (existingFile != null)
                {
                    throw new FileStoreException("File already exists and overwrite is not allowed.");
                }
            }

            var response = await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(path)
                .WithStreamData(inputStream)
                .WithObjectSize(inputStream.Length));
            
            if (response.Size != inputStream.Length)
            {
                throw new FileStoreException($"Failed to create file {path}: {response.Size}");
            }

            return path;
        }
        catch (MinioException ex)
        {
            throw new FileStoreException($"Failed to create file {path}: {ex.Message}", ex);
        }
    }
}