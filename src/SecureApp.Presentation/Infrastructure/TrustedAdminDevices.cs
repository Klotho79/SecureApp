namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// 2026-09-23, the user's own explicit ask right after wiping/reinstalling their own S23+ and
/// discovering the new "brand-new device defaults to Modifier" rule (see CurrentUserService's own
/// remarks) also demoted THEIR OWN primary devices: "na těchto dvou aplikacích tedy S23+ a PC bude
/// vždy admin" — these two specific devices should always bootstrap as Admin, every other device
/// (a genuinely new member's) still gets the safe Modifier floor. A plain hardcoded allowlist, not a
/// server-side policy: this is a client-only, first-run-only convenience for the app's own owner's
/// hardware, not a security boundary (RBAC here has never been anything but a local, self-declared
/// value — see <c>SecureApp.Domain.Enums.Role</c>'s own remarks on the "no multi-user/login concept
/// yet" model this sits on top of).
/// </summary>
internal static class TrustedAdminDevices
{
    /// <summary>Android: <c>Build.MODEL</c> as MAUI's <c>DeviceInfo.Current.Model</c> reports it (confirmed live: Samsung devices report the hyphenated form, e.g. "SM-G965F" for the project's own S9+ test device).</summary>
    private static readonly HashSet<string> TrustedAndroidModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "SM-S916B", // Samsung Galaxy S23+ — the user's own primary phone
    };

    /// <summary>Windows: the machine name (<see cref="Environment.MachineName"/>) — this dev PC.</summary>
    private static readonly HashSet<string> TrustedWindowsMachineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "DESKTOP-F8AS2U6",
    };

    public static bool IsThisDevice() => DeviceInfo.Current.Platform == DevicePlatform.WinUI
        ? TrustedWindowsMachineNames.Contains(Environment.MachineName)
        : TrustedAndroidModels.Contains(DeviceInfo.Current.Model);
}
