namespace SecureApp.Domain.Common;

/// <summary>
/// Base class for all Domain entities. Every entity is identified by a
/// client-generated <see cref="Guid"/> (never a database identity column)
/// so IDs can be created offline, before the encrypted row is ever
/// persisted to the local SQLCipher database.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ModifiedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;

    protected Entity() { }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Entity id cannot be empty.", nameof(id));

        Id = id;
    }

    /// <summary>Call after any mutation to keep <see cref="ModifiedAtUtc"/> accurate.</summary>
    protected void Touch() => ModifiedAtUtc = DateTimeOffset.UtcNow;

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
