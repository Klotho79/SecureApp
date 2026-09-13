using Microsoft.Extensions.DependencyInjection;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Keeps recently-opened chat/group pages alive and reuses the SAME instance when you reopen the same
/// conversation (2026-09-13, the user's repeated ask: "držet okna v paměti / vše má být načtené než to
/// otevřu"). Measurement proved the chat-open jank is fixed page-layout overhead (~180 ms per full
/// measure/layout pass, independent of message count, on BOTH 1:1 and group), paid every time because
/// pages are <c>AddTransient</c> and rebuilt from scratch. Reusing a page skips that rebuild on
/// revisit — provided the view model also skips repopulating unchanged content (see the
/// <c>ApplyQueryAttributes</c>/<c>LoadAsync</c> signature guards in ChatViewModel/GroupChatViewModel),
/// so the already-realized CollectionView cells stay put.
///
/// Shell's <see cref="RouteFactory.GetOrCreate(IServiceProvider)"/> is called synchronously inside
/// <c>GoToAsync</c> with no query attributes, so the navigating code sets <see cref="PendingKey"/>
/// (the session/group id) on the right factory immediately before navigating; that key selects the
/// cached instance. A tiny LRU bounds memory. A page currently still parented (shouldn't normally
/// happen — we navigate after a pop) is never reused; a fresh one is created instead.
/// </summary>
public sealed class CachedRouteFactory : RouteFactory
{
    private readonly Type _pageType;
    private readonly int _max;
    private readonly LinkedList<(string Key, Page Page)> _lru = new();
    private readonly object _gate = new();

    /// <summary>The target conversation id for the next navigation, set by the navigating code just before <c>GoToAsync</c>.</summary>
    public string? PendingKey;

    public CachedRouteFactory(Type pageType, int max = 4)
    {
        _pageType = pageType;
        _max = max;
    }

    public override Element GetOrCreate() => GetOrCreate(IPlatformApplication.Current!.Services);

    public override Element GetOrCreate(IServiceProvider services)
    {
        var key = PendingKey;
        PendingKey = null;

        lock (_gate)
        {
            if (key is not null)
            {
                for (var node = _lru.First; node is not null; node = node.Next)
                {
                    if (node.Value.Key != key) continue;
                    var cached = node.Value.Page;
                    if (cached.Parent is null) // safe to re-push only once it has been popped
                    {
                        _lru.Remove(node);
                        _lru.AddFirst((key, cached));
                        AppLog.Event("page.reuse.hit", ("type", _pageType.Name), ("key", key));
                        return cached;
                    }
                    break; // still parented — fall through and make a fresh one
                }
            }

            var created = (Page)services.GetRequiredService(_pageType);
            if (key is not null)
            {
                _lru.AddFirst((key, created));
                while (_lru.Count > _max) _lru.RemoveLast();
            }
            AppLog.Event("page.reuse.miss", ("type", _pageType.Name), ("key", key ?? "(none)"));
            return created;
        }
    }
}
