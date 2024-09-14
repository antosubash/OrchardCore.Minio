namespace OrchardCore.Media.Minio.ViewModels;

public class OptionsViewModel
{
    public string BucketName { get; set; }

    public string BasePath { get; set; }

    public bool CreateBucket { get; set; }
}