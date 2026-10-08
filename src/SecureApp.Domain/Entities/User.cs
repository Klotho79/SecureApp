using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;
using System.Linq;

namespace SecureApp.Domain.Entities;

/// <summary>
/// The local device's current user: a display name plus an app-wide <see cref="Role"/>.
/// Exactly one instance is ever persisted right now (see <c>IUserRepository</c>'s remarks) —
/// this is not yet a multi-user account system.
///
/// 2026-10-08, user's own ask: reconcile this device's identity against the shared "Soukromé
/// kontakty ARIM" list. <see cref="Nick"/> (the old <c>DisplayName</c> — kept the same storage/
/// name so every existing chat/device-list/contact-card call site needs zero changes) is now
/// OPTIONAL: <see cref="DisplayName"/> itself became a computed property that falls back to
/// <see cref="FormalName"/> (<see cref="FirstName"/> + <see cref="LastName"/>) whenever no nick
/// is set, so every existing consumer gets correct behavior automatically. <see cref="Phone"/>/
/// <see cref="Email"/> are new, used only for ARIM reconciliation (<c>SettingsViewModel</c>'s
/// save flow) and never shown/used anywhere else yet.
/// </summary>
public sealed class User : Entity
{
    /// <summary>The raw nick, exactly as typed — may be empty. Prefer <see cref="DisplayName"/> for anything user-facing. A real (private-set) property, not a bare field, so <c>EntityMaterializer</c> (property-reflection-only) can hydrate it like every other entity property.</summary>
    public string Nick { get; private set; }

    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public Role Role { get; private set; }

    /// <summary>Jméno+Příjmení joined with a space — empty if neither is set.</summary>
    public string FormalName => string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Every existing call site (chat bubbles, device lists, contact cards) reads this — nick if set, else the formal name, so none of them need to know the nick/name split exists.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Nick) ? FormalName : Nick;

    private User()
    {
        Nick = string.Empty;
    }

    public User(string displayName, Role role)
    {
        Nick = displayName ?? string.Empty;
        Role = role;
    }

    /// <summary>
    /// Sets the nick (2026-10-08: now allowed to be empty — <see cref="DisplayName"/> falls back to
    /// <see cref="FormalName"/> in that case). Kept the name "Rename" for every existing call site.
    /// </summary>
    public void Rename(string newDisplayName)
    {
        Nick = newDisplayName ?? string.Empty;
        Touch();
    }

    public void ChangeRole(Role newRole)
    {
        Role = newRole;
        Touch();
    }

    /// <summary>2026-10-08 — the ARIM-reconciliation profile fields. Each parameter null leaves that field unchanged (so a partial update, e.g. just correcting Phone, doesn't wipe the others).</summary>
    public void UpdateProfile(string? firstName, string? lastName, string? phone, string? email)
    {
        if (firstName is not null) FirstName = firstName;
        if (lastName is not null) LastName = lastName;
        if (phone is not null) Phone = phone;
        if (email is not null) Email = email;
        Touch();
    }
}
