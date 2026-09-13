using Microsoft.Maui.Storage;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation;

public partial class AppShell : Shell
{
	/// <summary>Route factories that keep chat/group pages in memory and reuse them on revisit (2026-09-13) — see CachedRouteFactory. The navigating code sets <c>PendingKey</c> on the matching one right before GoToAsync.</summary>
	public static readonly CachedRouteFactory ChatPageFactory = new(typeof(Views.ChatPage));
	public static readonly CachedRouteFactory GroupChatPageFactory = new(typeof(Views.GroupChatPage));

	/// <summary>Shared with <c>SettingsViewModel</c>'s own show/hide toggle for the Logbook tab — see <see cref="ApplyLogbookTabVisibility"/>'s own remarks.</summary>
	public const string LogbookVisibilityPreferenceKey = "logbook_visible";

	/// <summary>Nastavení is XAML index 4 (Chaty/Knihovna/Dokumenty/Kontakty/Nastavení, after the 2026-09-10 Kontakty tab landed at index 3) — inserting the Logbook tab at that same index pushes it one slot right instead of landing after it, the user's own explicit ask: "nastavení bych nechal jako poslední" (keep Settings last).</summary>
	private const int LogbookTabInsertIndex = 4;

	private Tab? _logbookTab;

	public AppShell()
	{
		InitializeComponent();

		// Logbook (2026-09-09) — shown as a tab only when enabled in Settings ("v nastavení přidej
		// možnost zobrazení a schování logbooku" — the user's own explicit ask). See
		// ApplyLogbookTabVisibility's own remarks for how this now applies live (2026-09-10).
		ApplyLogbookTabVisibility(Preferences.Default.Get(LogbookVisibilityPreferenceKey, false));

		// DocumentBrowserPage, ChatListPage, LibraryPage, and SettingsPage are the four
		// TabBar sections declared directly in AppShell.xaml — no RegisterRoute needed for
		// those, Shell resolves tab ShellContents on its own. Only detail/pushed pages
		// (reached via GoToAsync, not tab taps) need an explicit route registration here.

		// Reached via GoToAsync from DocumentBrowserPage, carrying a documentId query
		// parameter that DocumentViewerViewModel.ApplyQueryAttributes picks up.
		Routing.RegisterRoute(nameof(DocumentViewerPage), typeof(DocumentViewerPage));

		// Reached via the '+ New Chat' button on ChatListPage.
		Routing.RegisterRoute(nameof(NewChatPage), typeof(NewChatPage));

		// Reached via GoToAsync from ChatListPage/NewChatPage, carrying a chatSessionId query
		// parameter that ChatViewModel.ApplyQueryAttributes picks up. Registered with a caching factory
		// (2026-09-13) so reopening the same chat reuses the already-built page instead of rebuilding it.
		Routing.RegisterRoute(nameof(ChatPage), ChatPageFactory);

		// Group chats (2026-09-07) — reached via the '+ Nová skupina' button on ChatListPage.
		Routing.RegisterRoute(nameof(NewGroupPage), typeof(NewGroupPage));

		// Reached via GoToAsync from ChatListPage/NewGroupViewModel, carrying a groupChatId query
		// parameter that GroupChatViewModel.ApplyQueryAttributes picks up. Caching factory (2026-09-13),
		// same reasoning as ChatPage above.
		Routing.RegisterRoute(nameof(GroupChatPage), GroupChatPageFactory);

		// Logbook (2026-09-09) — reached via GoToAsync from LogbookPage, carrying a checklistId
		// query parameter that LogbookChecklistViewModel.ApplyQueryAttributes picks up. LogbookPage
		// itself is the 5th TabBar tab added conditionally above, not a registered route.
		Routing.RegisterRoute(nameof(LogbookChecklistPage), typeof(LogbookChecklistPage));

		// Logbook catalog management (2026-09-10) — split out of LogbookPage itself (the user's own
		// ask: keep daily-use actions — recording a procedure, ticking a checklist — off the same
		// screen as admin/modifier catalog edits). Reached via a button on LogbookPage, no query
		// parameters.
		Routing.RegisterRoute(nameof(LogbookManagePage), typeof(LogbookManagePage));
	}

	/// <summary>
	/// Adds or removes the Logbook tab from the TabBar LIVE (2026-09-10, user's own ask: "aby se
	/// změny v nastavení projevili hned a ne až po restartu") — replaces the earlier "read once at
	/// Shell construction, takes effect next launch" behavior. Manipulating <c>TabBar.Items</c>
	/// directly (Insert/Remove) is a live, Shell-observed collection — the earlier documented
	/// limitation was about toggling a <see cref="Tab"/>'s own <c>IsVisible</c> after the fact
	/// (unreliable across this app's own platform testing), not about inserting/removing the Tab
	/// object itself, which this always could have done. Called both from the constructor (seeded
	/// from the persisted preference) and immediately from <c>SettingsViewModel.OnIsLogbookVisibleChanged</c>
	/// whenever the toggle flips, on whichever device the user actually changed it on — this is
	/// still a genuinely per-device display preference (see <c>IsLogbookVisible</c>'s own remarks),
	/// not something the Logbook catalog sync (<c>ILogbookCatalogSyncService</c>) propagates to
	/// other devices.
	/// </summary>
	public void ApplyLogbookTabVisibility(bool visible)
	{
		if (Items.Count == 0 || Items[0] is not TabBar tabBar) return;

		if (visible && _logbookTab is null)
		{
			_logbookTab = new Tab
			{
				Title = "📓 Logbook",
				Items = { new ShellContent { ContentTemplate = new DataTemplate(typeof(LogbookPage)), Route = "LogbookTab" } }
			};
			tabBar.Items.Insert(Math.Min(LogbookTabInsertIndex, tabBar.Items.Count), _logbookTab);
		}
		else if (!visible && _logbookTab is not null)
		{
			tabBar.Items.Remove(_logbookTab);
			_logbookTab = null;
		}
	}
}
