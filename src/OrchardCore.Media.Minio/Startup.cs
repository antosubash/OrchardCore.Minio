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
    private readonly ILogger _logger;
    private readonly IShellConfiguration _configuration;

    public Startup(IShellConfiguration configuration,
        ILogger<Startup> logger)
        => (_configuration, _logger)
            = (configuration, logger);
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IPermissionProvider, Permissions>();
        services.AddScoped<INavigationProvider, AdminMenu>();
        services.AddTransient<IConfigureOptions<MinioStorageOptions>, MinioStorageOptionsConfiguration>();
            
        services.Configure<MinioStorageOptions>(options =>
        {
            options.BindConfiguration(MinioConstants.ConfigSection.Minio, _configuration, _logger);
        });
        
        // Replace the default media file provider with the media cache file provider.
        services.Replace(ServiceDescriptor.Singleton<IMediaFileProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>()));

        // Register the media cache file provider as a file store cache provider.
        services.AddSingleton<IMediaFileStoreCache>(serviceProvider =>
            serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>());
            
        services.AddMinio(config =>
        {
            var options = _configuration.GetSection(MinioConstants.ConfigSection.Minio).Get<MinioStorageOptions>();
            if (options == null)
            {
                _logger.LogError("No Minio configuration section found");
                return;
            }
            config.WithEndpoint(options.Endpoint)
                .WithSSL(false)
                .WithCredentials(options.AccessKey, options.SecretKey).Build();
        });
        
        services.AddSingleton<IMediaFileStoreCacheFileProvider>(serviceProvider =>
        {
            var hostingEnvironment = serviceProvider.GetRequiredService<IWebHostEnvironment>();

            if (string.IsNullOrWhiteSpace(hostingEnvironment.WebRootPath))
            {
                throw new MediaConfigurationException("The wwwroot folder for serving cache media files is missing.");
            }

            var mediaOptions = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
            var shellSettings = serviceProvider.GetRequiredService<ShellSettings>();
            var logger = serviceProvider.GetRequiredService<ILogger<DefaultMediaFileStoreCacheFileProvider>>();

            var mediaCachePath = GetMediaCachePath(
                hostingEnvironment, shellSettings, DefaultMediaFileStoreCacheFileProvider.AssetsCachePath);

            if (!Directory.Exists(mediaCachePath))
            {
                Directory.CreateDirectory(mediaCachePath);
            }

            return new DefaultMediaFileStoreCacheFileProvider(logger, mediaOptions.AssetsRequestPath, mediaCachePath);
        });
            
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
                
            var fileStore = new MinioFileStore(clock, options, minioClient);
            var mediaUrlBase = $"/{fileStore.Combine(shellSettings.RequestUrlPrefix, mediaOptions.AssetsRequestPath)}";

            var originalPathBase = sp.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?.Features.Get<ShellContextFeature>()
                ?.OriginalPathBase ?? PathString.Empty;

            if (originalPathBase.HasValue)
            {
                mediaUrlBase = fileStore.Combine(originalPathBase.Value, mediaUrlBase);
            }
                
            return new DefaultMediaFileStore(fileStore,
                mediaUrlBase,
                mediaOptions.CdnBaseUrl,
                mediaEventHandlers,
                mediaCreatingEventHandlers,
                logger);
        }));
        
        services.AddSingleton<IMediaEventHandler, DefaultMediaFileStoreCacheEventHandler>();

        //services.AddScoped<IModularTenantEvents, MinioTenantEvent>();
           
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