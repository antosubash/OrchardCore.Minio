namespace OrchardCore.Media.Minio.ViewModels;

public class OptionsViewModel
{
    public required string BucketName { get; set; }

    public required string BasePath { get; set; }

    public bool CreateBucket { get; set; }
}