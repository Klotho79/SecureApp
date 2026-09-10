using Microsoft.Maui.Storage;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation;

public partial class AppShell : Shell
{
	/// <summary>Shared with <c>SettingsViewModel</c>'s own show/hide toggle for the Logbook tab — see this constructor's own remarks on why toggling only takes effect on next launch.</summary>
	public const string LogbookVisibilityPreferenceKey = "logbook_visible";

	public AppShell()
	{
		InitializeComponent();

		// Logbook (2026-09-09) — shown as a tab only when enabled in Settings ("v nastavení přidej
		// možnost zobrazení a schování logbooku" — the user's own explicit ask). Read once, here,
		// at Shell construction time: .NET MAUI's TabBar has no platform-reliable way to toggle one
		// tab's visibility live while the Shell is already running, so flipping the Settings switch
		// takes effect the next time the app launches, not instantly — documented in SettingsPage's
		// own copy next to the toggle. Inserted BEFORE Nastavení, not appended after it — the
		// user's own explicit follow-up ask: "nastavení bych nechal jako poslední" (keep Settings
		// last). Nastavení is XAML index 3 (Chaty/Knihovna/Dokumenty/Nastavení); inserting at that
		// same index pushes it one slot right instead of landing after it.
		if (Preferences.Default.Get(LogbookVisibilityPreferenceKey, false) && Items.Count > 0 && Items[0] is TabBar tabBar)
		{
			tabBar.Items.Insert(3, new Tab
			{
				Title = "📓 Logbook",
				Items = { new ShellContent { ContentTemplate = new DataTemplate(typeof(LogbookPage)), Route = "LogbookTab" } }
			});
		}

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
		// parameter that ChatViewModel.ApplyQueryAttributes picks up.
		Routing.RegisterRoute(nameof(ChatPage), typeof(ChatPage));

		// Group chats (2026-09-07) — reached via the '+ Nová skupina' button on ChatListPage.
		Routing.RegisterRoute(nameof(NewGroupPage), typeof(NewGroupPage));

		// Reached via GoToAsync from ChatListPage/NewGroupViewModel, carrying a groupChatId query
		// parameter that GroupChatViewModel.ApplyQueryAttributes picks up.
		Routing.RegisterRoute(nameof(GroupChatPage), typeof(GroupChatPage));

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
}
