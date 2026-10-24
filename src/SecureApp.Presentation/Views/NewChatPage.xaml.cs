using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

// 2026-09-16 (user's own ask: "1:1 by mel byt jako skupinovy") — the manual contact-card/invite-blob
// paste+QR-scan fallback (and its camera setup, BarcodesDetected handlers) was removed from the XAML;
// the directory one-tap list is now the only path, mirroring how adding a group member already works
// (GroupChatViewModel.LoadAddableMembersAsync has no manual fallback either). The view-model half of
// the old fallback was deleted on 2026-10-24.
public partial class NewChatPage : ContentPage
{
    private readonly NewChatViewModel _viewModel;

    public NewChatPage(NewChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadMembersCommand.Execute(null);
    }
}
