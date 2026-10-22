using System.Collections.Concurrent;
using System.Reflection;
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
    // Loading a chat hydrates thousands of rows x ~17 properties each; the PropertyInfo/ConstructorInfo
    // lookups are cached so only the (unavoidable) reflective SetValue/Invoke remains per call.
    private static readonly ConcurrentDictionary<Type, ConstructorInfo> Constructors = new();
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo> Properties = new();

    public static T Create<T>() where T : Entity
    {
        var constructor = Constructors.GetOrAdd(typeof(T), type =>
            type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, Type.EmptyTypes, modifiers: null)
            ?? throw new InvalidOperationException($"{type.Name} has no parameterless constructor reachable via reflection."));
        return (T)constructor.Invoke(null);
    }

    public static void Set<T>(T entity, string propertyName, object? value) where T : Entity
    {
        var property = Properties.GetOrAdd((typeof(T), propertyName), key =>
            key.Type.GetProperty(key.Name)
            ?? throw new InvalidOperationException($"{key.Type.Name} has no property named '{key.Name}'."));
        property.SetValue(entity, value);
    }
}
