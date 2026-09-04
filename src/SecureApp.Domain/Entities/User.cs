using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Entities;

/// <summary>
/// The local device's current user: a display name plus an app-wide <see cref="Role"/>.
/// Exactly one instance is ever persisted right now (see <c>IUserRepository</c>'s remarks) —
/// this is not yet a multi-user account system.
/// </summary>
public sealed class User : Entity
{
    public string DisplayName { get; private set; }
    public Role Role { get; private set; }

    private User()
    {
        DisplayName = string.Empty;
    }

    public User(string displayName, Role role)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty.", nameof(displayName));

        DisplayName = displayName;
        Role = role;
    }

    public void Rename(string newDisplayName)
    {
        if (string.IsNullOrWhiteSpace(newDisplayName))
            throw new ArgumentException("Display name cannot be empty.", nameof(newDisplayName));

        DisplayName = newDisplayName;
        Touch();
    }

    public void ChangeRole(Role newRole)
    {
        Role = newRole;
        Touch();
    }
}
