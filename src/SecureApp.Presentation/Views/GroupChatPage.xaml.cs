using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>
/// Thin push-navigation host for <see cref="GroupChatThreadView"/> (2026-09-13) — mirrors
/// <see cref="ChatPage"/> for 1:1. The group UI moved into the reusable view so it can also be hosted
/// persistently (shown/hidden) in <see cref="ChatListPage"/>'s phone overlay without being rebuilt each
/// open. This page just drives the Load/StartListening/StopListening lifecycle for the pushed case
/// (e.g. right after creating a group).
/// </summary>
public partial class GroupChatPage : ContentPage
{
    private readonly GroupChatViewModel _viewModel;

    public GroupChatPage(GroupChatViewModel viewModel, ISharedLibraryService libraryService)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        Content = new GroupChatThreadView(libraryService) { BindingContext = viewModel };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.StartListening();
        _viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopListening();
    }
}
