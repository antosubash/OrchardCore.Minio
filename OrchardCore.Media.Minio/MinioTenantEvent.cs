using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Minio;
using OrchardCore.Environment.Shell;
using OrchardCore.FileStorage.Minio;
using OrchardCore.Modules;

namespace OrchardCore.Media.Minio;

public class MinioTenantEvent : ModularTenantEvents
{
    private readonly ShellSettings _shellSettings;
    private readonly MinioStorageOptions _options;
    private readonly IMinioClient _minioClient;
    private readonly IStringLocalizer S;
    private readonly ILogger _logger;

    protected MinioTenantEvent(
        ShellSettings shellSettings,
        IMinioClient minioClient,
        MinioStorageOptions options,
        IStringLocalizer localizer,
        ILogger logger)
    {
        _shellSettings = shellSettings;
        _minioClient = minioClient;
        _options = options;
        S = localizer;
        _logger = logger;
    }
}