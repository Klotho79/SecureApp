using Microsoft.Maui.Devices;

namespace SecureApp.Presentation.HomeAi;

/// <summary>
/// Which device may see the private home-AI chat (2026-11-10, user's ask: "jen na můj tel, to je S23+, a jen tento").
/// Not every Admin phone: only a Samsung Galaxy S23+ (model SM-S916B). Together with the Admin role check at the call
/// sites this leaves exactly the user's own phone — other members, the S9+ and the PCs never get the entry or the page.
/// </summary>
internal static class HomeAiAccess
{
    private const string AllowedModel = "SM-S916B";

    public static bool IsAllowedDevice
        => DeviceInfo.Current.Platform == DevicePlatform.Android
           && string.Equals(Normalize(DeviceInfo.Current.Model), AllowedModel, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? model) => (model ?? string.Empty).Trim().Replace('_', '-').Replace(' ', '-');
}
