namespace OrchardCore.Media.Minio;

public class MinioConstants
{
    internal static class ValidationMessages
    {
        public const string BucketNameIsEmpty = "The bucket name is required.";
    }
    
    internal static class ConfigSection
    {
        public const string Minio = "OrchardCore_Media_Minio";
    }
}