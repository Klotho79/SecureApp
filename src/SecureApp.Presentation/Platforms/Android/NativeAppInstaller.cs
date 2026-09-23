using Android.Content;
using AndroidX.Core.Content;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Infrastructure;

/// <inheritdoc cref="INativeAppInstaller"/>
/// <remarks>
/// Android implementation: wraps the downloaded file in a content:// Uri via the FileProvider
/// declared in AndroidManifest.xml (authority <c>com.companyname.secureapp.presentation.updateprovider</c>,
/// see that file's own remarks — a raw file:// Uri handed to another app/component throws
/// FileUriExposedException on modern Android) and launches <c>ACTION_VIEW</c> against it. The OS's
/// own "install unknown apps" / package-install confirmation screen still appears — this never
/// bypasses that, it only gets the user there without them needing to dig through a Downloads/Files
/// app themselves.
/// </remarks>
public sealed class NativeAppInstaller : INativeAppInstaller
{
    private const string ProviderAuthority = "com.companyname.secureapp.presentation.updateprovider";

    public void InstallApk(string localFilePath)
    {
        var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
        var file = new Java.IO.File(localFilePath);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, ProviderAuthority, file);

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        context.StartActivity(intent);
    }
}
