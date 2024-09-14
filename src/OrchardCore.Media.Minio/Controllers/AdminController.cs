using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Options;
using OrchardCore.DisplayManagement.Notify;
using OrchardCore.FileStorage.Minio;
using OrchardCore.Media.Minio.ViewModels;

namespace OrchardCore.Media.Minio.Controllers;

public class AdminController(
    IAuthorizationService authorizationService,
    IOptions<MinioStorageOptions> options,
    INotifier notifier,
    IHtmlLocalizer<AdminController> htmlLocalizer)
    : Controller
{
    private readonly MinioStorageOptions _options = options.Value;
    private readonly IHtmlLocalizer _h = htmlLocalizer;

    public async Task<IActionResult> Options()
    {
            if (!await authorizationService.AuthorizeAsync(User, Permissions.ViewMinioMediaOptions))
            {
                return Forbid();
            }
            
            if (string.IsNullOrWhiteSpace(_options.AccessKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                await notifier.WarningAsync(_h["The Minio access key and secret key are not configured. Please configure the Minio access key and secret key."]);
            }
            
            var model = new OptionsViewModel
            {
                BucketName = _options.BucketName,
                CreateBucket = _options.CreateBucket
            };

            return View(model);
        }
}