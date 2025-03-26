using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Configuration;
using OrchardCore.FileStorage;
using OrchardCore.FileStorage.Minio;
using OrchardCore.Media.Core;
using OrchardCore.Media.Core.Events;
using OrchardCore.Media.Events;
using OrchardCore.Modules;
using OrchardCore.Navigation;
using OrchardCore.Security.Permissions;
using StartupBase = OrchardCore.Modules.StartupBase;

namespace OrchardCore.Media.Minio;

public class Startup : StartupBase
{
    private readonly ILogger<Startup> _logger;
    private readonly IShellConfiguration _configuration;

    public Startup(IShellConfiguration configuration, ILogger<Startup> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register core services
        services.AddScoped<IPermissionProvider, Permissions>();
        services.AddScoped<INavigationProvider, AdminMenu>();
        services.AddTransient<IConfigureOptions<MinioStorageOptions>, MinioStorageOptionsConfiguration>();
        
        // Configure MinIO options
        services.Configure<MinioStorageOptions>(options =>
        {
            options.BindConfiguration(MinioConstants.ConfigSection.Minio, _configuration, _logger);
        });
        
        // Configure MinIO client
        services.AddMinio(config =>
        {
            var options = _configuration.GetSection(MinioConstants.ConfigSection.Minio).Get<MinioStorageOptions>();
            if (options == null)
            {
                _logger.LogError("No MinIO configuration section found");
                return;
            }

            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                _logger.LogError("MinIO endpoint is not configured");
                return;
            }

            if (string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey))
            {
                _logger.LogError("MinIO credentials are not configured");
                return;
            }

            config.WithEndpoint(options.Endpoint)
                .WithSSL(options.Secure)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .Build();
        });

        // Configure media cache
        ConfigureMediaCache(services);
        
        // Configure media file store
        ConfigureMediaFileStore(services);
        
        // Register media event handlers
        services.AddSingleton<IMediaEventHandler, DefaultMediaFileStoreCacheEventHandler>();
    }

    private void ConfigureMediaCache(IServiceCollection services)
    {
        services.AddSingleton<IMediaFileStoreCacheFileProvider>(serviceProvider =>
        {
            var hostingEnvironment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
            var mediaOptions = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
            var shellSettings = serviceProvider.GetRequiredService<ShellSettings>();
            var logger = serviceProvider.GetRequiredService<ILogger<DefaultMediaFileStoreCacheFileProvider>>();

            if (string.IsNullOrWhiteSpace(hostingEnvironment.WebRootPath))
            {
                throw new MediaConfigurationException("The wwwroot folder for serving cache media files is missing.");
            }

            var mediaCachePath = GetMediaCachePath(
                hostingEnvironment, shellSettings, DefaultMediaFileStoreCacheFileProvider.AssetsCachePath);

            Directory.CreateDirectory(mediaCachePath);

            return new DefaultMediaFileStoreCacheFileProvider(logger, mediaOptions.AssetsRequestPath, mediaCachePath);
        });

        // Replace the default media file provider with the media cache file provider
        services.Replace(ServiceDescriptor.Singleton<IMediaFileProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>()));

        // Register the media cache file provider as a file store cache provider
        services.AddSingleton<IMediaFileStoreCache>(serviceProvider =>
            serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>());
    }

    private void ConfigureMediaFileStore(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IMediaFileStore>(sp =>
        {
            var shellSettings = sp.GetRequiredService<ShellSettings>();
            var options = sp.GetRequiredService<IOptions<MinioStorageOptions>>().Value;
            var clock = sp.GetRequiredService<IClock>();
            var minioClient = sp.GetRequiredService<IMinioClient>();
            var mediaOptions = sp.GetRequiredService<IOptions<MediaOptions>>().Value;
            var mediaEventHandlers = sp.GetServices<IMediaEventHandler>();
            var mediaCreatingEventHandlers = sp.GetServices<IMediaCreatingEventHandler>();
            var logger = sp.GetRequiredService<ILogger<DefaultMediaFileStore>>();
            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
                
            var fileStore = new MinioFileStore(clock, options, minioClient);
            var mediaUrlBase = $"/{fileStore.Combine(shellSettings.RequestUrlPrefix, mediaOptions.AssetsRequestPath)}";

            var originalPathBase = httpContextAccessor.HttpContext
                ?.Features.Get<ShellContextFeature>()
                ?.OriginalPathBase ?? PathString.Empty;

            if (originalPathBase.HasValue)
            {
                mediaUrlBase = fileStore.Combine(originalPathBase.Value, mediaUrlBase);
            }
                
            return new DefaultMediaFileStore(
                fileStore,
                mediaUrlBase,
                mediaOptions.CdnBaseUrl,
                mediaEventHandlers,
                mediaCreatingEventHandlers,
                logger);
        }));
    }

    public override void Configure(IApplicationBuilder builder, IEndpointRouteBuilder routes, IServiceProvider serviceProvider)
    {
        routes.MapAreaControllerRoute(
            name: "Home",
            areaName: "OrchardCore.Media.Minio",
            pattern: "Home/Index",
            defaults: new { controller = "Admin", action = "Index" }
        );
    }
    
    private static string GetMediaCachePath(IWebHostEnvironment hostingEnvironment, ShellSettings shellSettings, string assetsPath)
        => PathExtensions.Combine(hostingEnvironment.WebRootPath, shellSettings.Name, assetsPath);
}