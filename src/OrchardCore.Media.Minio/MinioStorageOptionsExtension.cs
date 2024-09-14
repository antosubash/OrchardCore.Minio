using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrchardCore.Environment.Shell.Configuration;
using OrchardCore.FileStorage.Minio;

namespace OrchardCore.Media.Minio;

public static class MinioStorageOptionsExtension
{
    private static IEnumerable<ValidationResult> Validate(this MinioStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccessKey))
        {
            yield return new ValidationResult("The access key is required.", new[] { nameof(options.AccessKey) });
        }
        
        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            yield return new ValidationResult("The secret key is required.", new[] { nameof(options.SecretKey) });
        }
        
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            yield return new ValidationResult("The endpoint is required.", new[] { nameof(options.Endpoint) });
        }
        
        if (options.CreateBucket && string.IsNullOrWhiteSpace(options.BucketName))
        {
            yield return new ValidationResult("The bucket name is required to create a bucket.", new[] { nameof(options.BucketName) });
        }
    }
    
    public static MinioStorageOptions BindConfiguration(this MinioStorageOptions  options, string configSection, IShellConfiguration shellConfiguration, ILogger logger)
    {
        var section = shellConfiguration.GetSection(configSection);

        try
        {
            if (!section.Exists()) return options;
            section.Bind(options);
            
            foreach (var result in options.Validate())
            {
                logger.LogError(result.ErrorMessage);
            }
        }
        finally
        {
            logger.LogError("An error occurred while binding the configuration section '{configSection}'.", configSection);
        }
        return options;
    }
}