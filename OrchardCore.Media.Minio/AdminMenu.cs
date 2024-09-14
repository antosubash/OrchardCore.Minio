using Microsoft.Extensions.Localization;
using OrchardCore.Navigation;

namespace OrchardCore.Media.Minio;

public class AdminMenu(IStringLocalizer<AdminMenu> localizer) : INavigationProvider
{
    private readonly IStringLocalizer _s = localizer;

    public async Task BuildNavigationAsync(string name, NavigationBuilder builder)
    {
        builder.Add(_s["Configuration"], configuration => configuration
            .Add(_s["Media"], _s["Media"].PrefixPosition(), media => media
                .Add(_s["Minio Options"], _s["Minio Options"].PrefixPosition(), options => options
                    .Action("Options", "Admin", "OrchardCore.Media.Minio")
                    .Permission(Permissions.ViewMinioMediaOptions)
                    .LocalNav()
                )
            )
        );
        await Task.CompletedTask;
    }
}