using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SecureApp.Presentation.Profiles;

/// <summary>
/// 2026-10-05 — which <see cref="ProfileRegistry.Profile"/> (if any) this process is running as,
/// decided once, read from a plain pointer file written by the PREVIOUS process right before it
/// restarted (<see cref="SetActiveAndRestart"/>). Windows-only; every other platform (and a Windows
/// launch with the <c>SECUREAPP_DATA_DIR</c> dev-testing override set, see <c>MauiProgram.cs</c>)
/// behaves exactly as before this feature existed.
///
/// Deliberately NOT a live, in-process "switch profile" mechanism: <c>DataStorageOptions</c> and
/// <c>MauiSecureVaultKeyStore</c> are both resolved as DI singletons exactly once per process, and
/// <c>SqlCipherConnectionFactory</c> caches its one SQLite connection forever after the first open —
/// none of that is safe to hot-swap mid-session. A profile change always restarts the process instead,
/// so every one of those singletons is built fresh against the newly-chosen profile.
/// </summary>
public static class ActiveProfile
{
    public static string BaseDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecureApp");

    private static string PointerPath => Path.Combine(BaseDirectory, "active-profile.txt");

    private static readonly Lazy<ProfileRegistry.Profile?> _current = new(LoadCurrent);

    /// <summary>True only on Windows, with no dev-testing override active, and no profile chosen yet — the one condition that should show <see cref="Views.ProfileLoginPage"/> instead of the normal app.</summary>
    public static bool RequiresLogin =>
        DeviceInfo.Current.Platform == DevicePlatform.WinUI
        && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SECUREAPP_DATA_DIR"))
        && _current.Value is null;

    public static ProfileRegistry.Profile? Current => _current.Value;

    public static bool IsOwnerProfile => _current.Value?.IsOwner ?? false;

    /// <summary>The data directory this process should use, or null to fall back to today's default (<c>FileSystem.AppDataDirectory</c>) — same precedence <c>MauiProgram.cs</c> already gives the <c>SECUREAPP_DATA_DIR</c> dev override.</summary>
    public static string? DataDirectoryOverride => _current.Value is { } profile
        ? Path.Combine(BaseDirectory, "Profiles", profile.FolderName)
        : null;

    /// <summary>Same derivation <c>MauiProgram.cs</c> already uses for the <c>SECUREAPP_DATA_DIR</c> dev override, just fed this profile's own folder path instead — no new hashing scheme invented.</summary>
    public static string? VaultKeyPrefix => DataDirectoryOverride is { } dir
        ? "profile:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(dir)))[..12]
        : null;

    /// <summary>Short, stable per-profile fragment the ~8 <c>Preferences</c>-backed stores prefix their own keys with (see those stores' own remarks) — empty when no profile is active, i.e. identical to today's un-prefixed keys.</summary>
    public static string PrefKey(string key) => VaultKeyPrefix is { } prefix ? $"{prefix}:{key}" : key;

    private static ProfileRegistry.Profile? LoadCurrent()
    {
        try
        {
            if (DeviceInfo.Current.Platform != DevicePlatform.WinUI) return null;
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SECUREAPP_DATA_DIR"))) return null;
            if (!File.Exists(PointerPath)) return null;

            var folderName = File.ReadAllText(PointerPath).Trim();
            if (folderName.Length == 0) return null;

            return ProfileRegistry.LoadAll().FirstOrDefault(p => string.Equals(p.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null; // Best-effort — an unreadable pointer just means "no profile active", never a crash.
        }
    }

    /// <summary>Writes the chosen profile's pointer and relaunches the exe as a fresh process — see this class' own remarks on why a live hot-swap isn't attempted. Never returns (the caller's own process exits).</summary>
    public static void SetActiveAndRestart(ProfileRegistry.Profile profile)
    {
        Directory.CreateDirectory(BaseDirectory);
        File.WriteAllText(PointerPath, profile.FolderName);
        Restart();
    }

    /// <summary>Logout — clears the pointer and relaunches, landing back on <see cref="Views.ProfileLoginPage"/>.</summary>
    public static void ClearAndRestart()
    {
        try { File.Delete(PointerPath); } catch { /* best-effort */ }
        Restart();
    }

    private static void Restart()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
            Process.Start(exePath);
        Environment.Exit(0);
    }
}
