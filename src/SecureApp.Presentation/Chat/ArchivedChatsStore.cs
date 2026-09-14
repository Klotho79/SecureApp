using Microsoft.Maui.Storage;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Per-device set of chats/groups that have been ARCHIVED (2026-09-14, the user's ask: "pokud chat
/// ztratí všechny uživatele, uloží se do archivu"). A group auto-archives when it loses every other
/// member; the user can also archive manually. An archived chat is moved out of the main list into a
/// local, read-only Archive section — its message history and (private) attachments stay in the local
/// DB, so it can still be browsed and its files promoted to the global community library
/// (see ISharedLibraryService.PublishToLibraryAsync).
///
/// Preferences-backed (a small set of chat/group ids, nothing secret) for the same DI-free,
/// no-migration reasons as <see cref="RemovedPeersStore"/>. The archive is LOCAL — each device archives
/// its own chats; there is no shared/server archive (that would break E2EE), so "admin/modifier/
/// participant may view it" is honored simply by it being this participant's own device.
/// </summary>
public static class ArchivedChatsStore
{
    private const string Key = "archived_chats_v1";
    private static readonly object _gate = new();

    private static HashSet<string> Load()
    {
        try
        {
            var raw = Preferences.Default.Get(Key, string.Empty);
            return string.IsNullOrEmpty(raw)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(raw.Split(';', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    private static void Save(HashSet<string> set)
    {
        try { Preferences.Default.Set(Key, string.Join(';', set)); }
        catch { /* best-effort */ }
    }

    public static void Add(Guid chatOrGroupId)
    {
        lock (_gate) { var s = Load(); if (s.Add(chatOrGroupId.ToString())) Save(s); }
    }

    public static void Remove(Guid chatOrGroupId)
    {
        lock (_gate) { var s = Load(); if (s.Remove(chatOrGroupId.ToString())) Save(s); }
    }

    public static bool Contains(Guid chatOrGroupId) => Load().Contains(chatOrGroupId.ToString());

    public static IReadOnlySet<Guid> All()
    {
        var result = new HashSet<Guid>();
        foreach (var s in Load())
            if (Guid.TryParse(s, out var id)) result.Add(id);
        return result;
    }
}
