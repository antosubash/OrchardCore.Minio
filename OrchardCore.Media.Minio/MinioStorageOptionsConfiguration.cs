using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.Environment.Shell.Configuration;
using OrchardCore.FileStorage.Minio;

namespace OrchardCore.Media.Minio;

public class MinioStorageOptionsConfiguration(
    IShellConfiguration shellConfiguration,
    ILogger<MinioStorageOptionsConfiguration> logger)
    : IConfigureOptions<MinioStorageOptions>
{
    public void Configure(MinioStorageOptions options)
    {
        options.BindConfiguration(MinioConstants.ConfigSection.Minio, shellConfiguration, logger);
    }
}