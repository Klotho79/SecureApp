using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// In-memory-cached access to the local device's current user (name + <see cref="Role"/>),
/// backed by <c>IUserRepository</c>. Registered as a singleton so every caller shares one
/// cached value for the app's lifetime.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// The current user. Valid immediately (a sensible in-memory default — Admin role —
    /// even before <see cref="InitializeAsync"/> has completed), so callers never need a
    /// null check; <see cref="InitializeAsync"/> reconciles it against whatever was
    /// actually persisted (or persists the default, on first run).
    /// </summary>
    User Current { get; }

    /// <summary>Raised after <see cref="SetCurrentUserAsync"/> successfully persists a change.</summary>
    event EventHandler? CurrentUserChanged;

    /// <summary>Idempotent: loads the persisted user once per process, or persists the in-memory default if none exists yet.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    Task SetCurrentUserAsync(string displayName, Role role, CancellationToken ct = default);

    /// <summary>
    /// 2026-10-08 — the new ARIM-reconciliation profile fields (Jméno/Příjmení/Telefon/Email), kept
    /// separate from <see cref="SetCurrentUserAsync"/> since it never touches Nick/Role. Each
    /// parameter null leaves that field unchanged.
    /// </summary>
    Task UpdateProfileAsync(string? firstName, string? lastName, string? phone, string? email, CancellationToken ct = default);
}
