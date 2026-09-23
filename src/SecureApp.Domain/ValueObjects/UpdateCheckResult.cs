namespace SecureApp.Domain.ValueObjects;

/// <summary>Result of <see cref="Interfaces.Services.IUpdateService.CheckForUpdateAsync"/> — <see cref="IsUpdateAvailable"/> compares the relay's own hosted versionCode against this device's own running versionCode (<c>Microsoft.Maui.ApplicationModel.AppInfo.Current.BuildString</c>, read where this is constructed since Domain itself must not reference MAUI types).</summary>
public sealed record UpdateCheckResult(bool IsUpdateAvailable, int? LatestVersionCode, string? LatestVersionName, string? ErrorMessage);
