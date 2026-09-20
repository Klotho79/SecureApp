using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class ContactsPage : ContentPage
{
    private readonly ContactsViewModel _viewModel;
    private Guid? _draggedContactId;

    public ContactsPage(ContactsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.RequestNavigate += OnRequestNavigate;
        _viewModel.RequestAddContact += OnRequestAddContact;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private async void OnRequestNavigate(string route) => await Shell.Current.GoToAsync(route);

    private async void OnRequestAddContact() => await Shell.Current.GoToAsync(nameof(AddContactPage));

    // Desktop drag-and-drop reorder (2026-09-20, user's own ask) — MAUI's DragGestureRecognizer/
    // DropGestureRecognizer have no plain bindable-command shape for the drag payload itself, so this
    // stays in code-behind (same "MAUI-touching glue in the Page" split ContactsViewModel's own
    // remarks describe) rather than trying to force it through XAML bindings.
    private void OnContactDragStarting(object? sender, DragStartingEventArgs e)
    {
        if ((sender as Element)?.BindingContext is PersonalContactItem item)
            _draggedContactId = item.Id;
    }

    private async void OnContactDrop(object? sender, DropEventArgs e)
    {
        if (_draggedContactId is not { } draggedId) return;
        if ((sender as Element)?.BindingContext is not PersonalContactItem target) return;
        _draggedContactId = null;
        await _viewModel.ReorderAsync(draggedId, target.Id);
    }
}
