using Microsoft.Maui.Storage;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation;

public partial class AppShell : Shell
{
	/// <summary>Route factories that keep chat/group pages in memory and reuse them on revisit (2026-09-13) — see CachedRouteFactory. The navigating code sets <c>PendingKey</c> on the matching one right before GoToAsync.</summary>
	public static readonly CachedRouteFactory ChatPageFactory = new(typeof(Views.ChatPage));
	public static readonly CachedRouteFactory GroupChatPageFactory = new(typeof(Views.GroupChatPage));

	/// <summary>Shared with <c>SettingsViewModel</c>'s own show/hide toggles — see <see cref="RebuildTabBar"/>'s own remarks. Key spelling kept unchanged from before this was generalized (2026-09-16) so an existing install's already-stored preference still applies.</summary>
	public const string LogbookVisibilityPreferenceKey = "logbook_visible";
	public const string ChatsTabVisibilityPreferenceKey = "tab_chaty_visible";
	public const string FilesTabVisibilityPreferenceKey = "tab_soubory_visible";
	public const string ContactsTabVisibilityPreferenceKey = "tab_kontakty_visible";
	public const string NotificationsTabVisibilityPreferenceKey = "tab_oznameni_visible";

	/// <summary>
	/// Every tab that can be hidden (2026-09-16, user's own ask: "chci mít možnost schovávat
	/// jednotlivé menu kromě settings samozřejmě") plus how, in the desired FINAL order — Nastavení
	/// itself is deliberately absent: it's declared directly in AppShell.xaml, always last, and
	/// RebuildTabBar never touches it, so there's always at least one tab left regardless of what
	/// the user hides. Logbook's own default (hidden until turned on) is unchanged; the other three
	/// default to visible, matching how they've always behaved before this toggle existed.
	/// </summary>
	private (Tab Tab, string PreferenceKey, bool DefaultVisible)[] _hideableTabs = null!;

	private Tab? _logbookTab;

	public AppShell()
	{
		InitializeComponent();

		// Logbook (2026-09-09) — its Tab object didn't exist in XAML before this pass (created here,
		// once, same as the others now are) since it was the only one ever hidden by default.
		_logbookTab = new Tab
		{
			Title = "📓 Logbook",
			Items = { new ShellContent { ContentTemplate = new DataTemplate(typeof(LogbookPage)), Route = "LogbookTab" } }
		};

		_hideableTabs =
		[
			(NotificationsTab, NotificationsTabVisibilityPreferenceKey, true),
			(ChatsTab, ChatsTabVisibilityPreferenceKey, true),
			(FilesTab, FilesTabVisibilityPreferenceKey, true),
			(ContactsTab, ContactsTabVisibilityPreferenceKey, true),
			(_logbookTab, LogbookVisibilityPreferenceKey, false),
		];
		RebuildTabBar();

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

		// Notification Hub (2026-09-20, see NOTIFICATION_HUB_SPEC.md) — reached via GoToAsync from
		// NotificationsPage, carrying a notificationId query parameter that
		// NotificationDetailViewModel.ApplyQueryAttributes picks up. NotificationsPage itself is the
		// leading TabBar tab added above, not a registered route.
		Routing.RegisterRoute(nameof(NotificationDetailPage), typeof(NotificationDetailPage));

		// Smart Search (2026-09-20, Phase 4 — see NOTIFICATION_HUB_SPEC.md) — reached via the 🔍
		// button on NotificationsPage; global, not tied to any one tab, so a plain pushed route
		// rather than a TabBar entry.
		Routing.RegisterRoute(nameof(SmartSearchPage), typeof(SmartSearchPage));

		// Moje kontakty (2026-09-20) — reached via the ➕ button on ContactsPage.
		Routing.RegisterRoute(nameof(AddContactPage), typeof(AddContactPage));
	}

	/// <summary>
	/// Sets one tab's show/hide preference and re-applies the whole TabBar live (2026-09-10, user's
	/// own ask for Logbook originally: "aby se změny v nastavení projevili hned a ne až po
	/// restartu"; generalized 2026-09-16 to every tab). Called from each of <c>SettingsViewModel</c>'s
	/// <c>OnIs*TabVisibleChanged</c> partial-property hooks — still a genuinely per-device display
	/// preference (see <c>IsLogbookVisible</c>'s own remarks), not something synced to other devices.
	/// </summary>
	public void ApplyTabVisibility(string preferenceKey, bool visible)
	{
		Preferences.Default.Set(preferenceKey, visible);
		RebuildTabBar();
	}

	/// <summary>
	/// Rebuilds the live TabBar from <see cref="_hideableTabs"/> against each entry's CURRENT
	/// preference value, in that list's fixed order, always inserted right before the last item
	/// (Nastavení, declared directly in AppShell.xaml and never touched here — so there's always at
	/// least one tab left no matter what's hidden). Manipulating <c>TabBar.Items</c> directly
	/// (Remove then Insert) is a live, Shell-observed collection — Remove on an item not currently
	/// present is a harmless no-op, which is what makes rebuilding from scratch on every call safe
	/// and simple instead of diffing old vs. new visibility. The earlier per-tab "toggle IsVisible
	/// after the fact" approach was dropped for Logbook specifically because it proved unreliable
	/// across this app's own platform testing — this Insert/Remove approach has always worked.
	/// </summary>
	private void RebuildTabBar()
	{
		if (Items.Count == 0 || Items[0] is not TabBar tabBar) return;

		foreach (var (tab, _, _) in _hideableTabs)
			tabBar.Items.Remove(tab);

		foreach (var (tab, preferenceKey, defaultVisible) in _hideableTabs)
		{
			if (Preferences.Default.Get(preferenceKey, defaultVisible))
				tabBar.Items.Insert(Math.Max(0, tabBar.Items.Count - 1), tab);
		}
	}
}
