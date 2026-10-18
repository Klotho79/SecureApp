using System.Diagnostics;
using System.IO.Compression;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Infrastructure;

/// <inheritdoc cref="INativeAppInstaller"/>
/// <remarks>
/// Windows implementation (2026-10-12) — the portable build has no OS-level "install" flow to hand
/// off to the way Android's ACTION_VIEW package installer does; <paramref name="localFilePath"/> here
/// is <c>secureapp-windows-portable.zip</c>, already downloaded by <c>IUpdateService</c>, and this
/// must extract it over the files the RUNNING .exe is itself loaded from
/// (<see cref="AppContext.BaseDirectory"/>) — which Windows refuses to overwrite while they're in use.
///
/// Worked around the same way <c>Profiles.ActiveProfile.Restart</c> already relaunches this app after
/// a profile switch (<c>Process.Start</c> + <c>Environment.Exit(0)</c>), just with a short-lived
/// PowerShell helper in between that waits for THIS process to actually exit (by PID, not merely
/// "launched") before copying anything, same self-fetching-script spirit as
/// <c>relay/ops/new-pc-onboarding.ps1</c>. The zip is flat (no wrapping top-level folder — confirmed
/// against that same onboarding script's own <c>Expand-Archive</c> call), so a plain recursive copy
/// of its extracted contents over the install directory is enough; nothing here needs to know what's
/// actually inside it.
/// </remarks>
public sealed class NativeAppInstaller : INativeAppInstaller
{
    public void InstallApk(string localFilePath)
    {
        var installDir = AppContext.BaseDirectory.TrimEnd('\\');
        var stagingDir = Path.Combine(Path.GetTempPath(), "SecureAppUpdate-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(localFilePath, stagingDir);

        var exePath = Environment.ProcessPath ?? Path.Combine(installDir, "SecureApp.Presentation.exe");
        var scriptPath = Path.Combine(Path.GetTempPath(), "secureapp-update-" + Guid.NewGuid().ToString("N") + ".ps1");
        var pid = Environment.ProcessId;

        // Every path is already a resolved local path (TEMP/install dir), never user input — the
        // single quotes below are only to survive a space in the install dir (the default is
        // "C:\SecureApp", but relay/ops/new-pc-onboarding.ps1's own -InstallDir can be anything).
        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            try { Wait-Process -Id {{pid}} -Timeout 30 } catch {}
            Start-Sleep -Milliseconds 500
            Copy-Item -Path '{{stagingDir}}\*' -Destination '{{installDir}}' -Recurse -Force
            Remove-Item -Path '{{stagingDir}}' -Recurse -Force
            Remove-Item -Path '{{localFilePath}}' -Force
            Start-Process -FilePath '{{exePath}}' -WorkingDirectory '{{installDir}}'
            Remove-Item -Path '{{scriptPath}}' -Force
            """;
        // UTF-8 WITH BOM: Windows PowerShell 5.1 reads a BOM-less script as ANSI, which turned an install
        // dir like "K:\Meduňa\Appka" into mojibake so nothing was copied and the app never relaunched.
        File.WriteAllText(scriptPath, script, new System.Text.UTF8Encoding(true));

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        // Never returns, same contract ActiveProfile.Restart already has — the update can't actually
        // land until this process (and its lock on every file in installDir) is gone.
        Environment.Exit(0);
    }
}
