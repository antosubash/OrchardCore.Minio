namespace OrchardCore.FileStorage.Minio;

public class MinioStorageOptions
{
    public string BucketName { get; set; }
    public string AccessKey { get; set; }
    public string SecretKey { get; set; }
    public string Endpoint { get; set; }
    public bool Secure { get; set; }
    public bool CreateBucket { get; set; }
}