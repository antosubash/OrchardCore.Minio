using OrchardCore.Security.Permissions;

namespace OrchardCore.Media.Minio;

public class Permissions : IPermissionProvider
{
    public static readonly Permission ViewMinioMediaOptions = new Permission("ViewMinioMediaOptions", "View Minio Media Options");
    
    private readonly IEnumerable<Permission> _permissions = new[]
    {
        ViewMinioMediaOptions
    };
    
    public async Task<IEnumerable<Permission>> GetPermissionsAsync()
    {
        return await Task.FromResult(_permissions);
    }

    public IEnumerable<PermissionStereotype> GetDefaultStereotypes()
    {
        return new[]
        {
            new PermissionStereotype
            {
                Name = "Administrator",
                Permissions = _permissions
            }
        };
    }
}