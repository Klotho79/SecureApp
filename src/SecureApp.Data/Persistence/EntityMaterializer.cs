using SecureApp.Domain.Common;

namespace SecureApp.Data.Persistence;

/// <summary>
/// Reflection-based hydration for Domain entities. Every entity's constructor is
/// either public-with-validation (for creating a *new* one) or a private
/// parameterless one explicitly "reserved for materialization by persistence
/// infrastructure" (see e.g. <c>Document</c>'s private ctor comment) — and every
/// property setter is non-public. Repositories are the one place allowed to reach
/// past that encapsulation to rebuild an entity from a database row, via this class,
/// rather than re-running the public constructor's creation-time validation against
/// data that's already been validated once (at the point it was first inserted).
/// </summary>
internal static class EntityMaterializer
{
    public static T Create<T>() where T : Entity
        => (T)(Activator.CreateInstance(typeof(T), nonPublic: true)
            ?? throw new InvalidOperationException($"{typeof(T).Name} has no parameterless constructor reachable via reflection."));

    public static void Set<T>(T entity, string propertyName, object? value) where T : Entity
    {
        var property = typeof(T).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"{typeof(T).Name} has no property named '{propertyName}'.");
        property.SetValue(entity, value);
    }
}
