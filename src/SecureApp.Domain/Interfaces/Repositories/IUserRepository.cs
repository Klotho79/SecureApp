using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

/// <summary>
/// Persists the single local "current user" record. Not a multi-user table — see
/// <see cref="User"/>'s remarks and DEVELOPMENT_PLAN.md's Milestone 3 note: single
/// current-user-per-device model, multi-user/per-folder role assignment deliberately
/// deferred rather than decided against.
/// </summary>
public interface IUserRepository
{
    /// <summary>Null if no user has ever been saved on this device (e.g. first run).</summary>
    Task<User?> GetCurrentUserAsync(CancellationToken ct = default);

    /// <summary>Replaces whatever user record exists (there is only ever one) with <paramref name="user"/>.</summary>
    Task SaveCurrentUserAsync(User user, CancellationToken ct = default);
}
