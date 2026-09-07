using SecureApp.Presentation.Views;

namespace SecureApp.Presentation;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

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
	}
}
