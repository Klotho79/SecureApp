using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Data.Identity;

/// <inheritdoc cref="ICurrentUserService"/>
public sealed class CurrentUserService : ICurrentUserService
{
    private const string FallbackDisplayName = "Local User";

    private readonly IUserRepository _userRepository;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly bool _isTrustedAdminDevice;
    private User _current;
    private bool _initialized;

    public event EventHandler? CurrentUserChanged;

    public User Current => _current;

    /// <summary>
    /// <paramref name="defaultDisplayName"/> (2026-09-19) — a never-configured or freshly-reset
    /// device (see <c>TransportEndpointConfiguration</c>'s own remarks on identity resets) used to
    /// silently show up everywhere — the relay directory, every group roster, every chat — as the
    /// literal placeholder "Local User" until a human happened to open Settings and typed a real
    /// name. The user's own explicit standing requirement ("uzivatel vůbec nema poznat ze je neco
    /// spatne") rules that out: a device must never need a human to give it a presentable name.
    /// Presentation passes the OS-reported device name (<c>DeviceInfo.Current.Name</c>) here — the
    /// Data layer itself stays platform-agnostic (same reasoning as <see cref="SecureApp.Data.DataStorageOptions"/>),
    /// so this is supplied, not computed. Falls back to <see cref="FallbackDisplayName"/> only if
    /// that's ever null/blank (e.g. a platform that reports nothing).
    /// </summary>
    /// <param name="isTrustedAdminDevice">
    /// 2026-09-23, same-day follow-up to the Modifier-default correction below: the user's own
    /// explicit ask — "na těchto dvou aplikacích tedy S23+ a PC bude vždy admin" — their own two
    /// primary devices must always bootstrap as Admin, never Modifier, while every genuinely new
    /// member's device still gets the safe Modifier floor. Computed by the caller (Presentation,
    /// matching device model/machine name against a known-trusted list — see MauiProgram's own
    /// remarks), never hardcoded here: this class stays platform-agnostic (same reasoning
    /// <paramref name="defaultDisplayName"/> is supplied rather than computed).
    /// </param>
    public CurrentUserService(IUserRepository userRepository, string? defaultDisplayName = null, bool isTrustedAdminDevice = false)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _isTrustedAdminDevice = isTrustedAdminDevice;
        var displayName = string.IsNullOrWhiteSpace(defaultDisplayName) ? FallbackDisplayName : defaultDisplayName;
        // Sensible in-memory default so Current is never null before InitializeAsync completes.
        //
        // 2026-09-23 correction: this used to default to Admin ("so existing single-user
        // functionality isn't suddenly locked down for anyone upgrading from before RBAC existed")
        // — fine while every install was this dev's own, but the app now has a real onboarding flow
        // (Settings' QR/link, self-update system) bringing in genuinely new people, and every one of
        // them silently landed as Admin on first launch. User's own explicit rule (2026-09-23): "nový
        // člen nebude nikdy admin" — a brand-new device must never default to Admin. Modifier is the
        // new floor: normal day-to-day work (create/edit) without admin-only actions (relay deploy,
        // invite minting, device management) — see SettingsViewModel's own AvailableRoles remarks for
        // the matching guard against a non-Admin device just picking Admin from the Role picker.
        // isTrustedAdminDevice (same day, see its own param doc) is the one deliberate exception.
        _current = new User(displayName, isTrustedAdminDevice ? Role.Admin : Role.Modifier);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var stored = await _userRepository.GetCurrentUserAsync(ct);
            if (stored is null)
            {
                await _userRepository.SaveCurrentUserAsync(_current, ct);
            }
            else
            {
                _current = stored;

                // 2026-09-23, real gap found same day as the trusted-device bootstrap above: Android
                // Auto Backup (allowBackup=true) can silently RESTORE a stale User row (from before
                // this device was ever specifically trusted, or from a deliberate earlier RBAC test —
                // this exact device had briefly been set to Viewer for testing) onto a device that
                // never actually went through the "stored is null" bootstrap path at all, since
                // restore happens before this code ever runs. The one-time bootstrap default above
                // can't fix that; a trusted device's role is enforced HERE, every InitializeAsync, not
                // just the first ever run — "vždy admin" (always admin), the user's own words, taken
                // literally rather than "admin only if nothing else got there first."
                if (_isTrustedAdminDevice && _current.Role != Role.Admin)
                {
                    _current.ChangeRole(Role.Admin);
                    await _userRepository.SaveCurrentUserAsync(_current, ct);
                }
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SetCurrentUserAsync(string displayName, Role role, CancellationToken ct = default)
    {
        _current.Rename(displayName);
        _current.ChangeRole(role);
        await _userRepository.SaveCurrentUserAsync(_current, ct);
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
    }
}
