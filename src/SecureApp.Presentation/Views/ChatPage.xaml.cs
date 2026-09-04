using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel _viewModel;
    private readonly ISharedLibraryService _libraryService;

    public ChatPage(ChatViewModel viewModel, ISharedLibraryService libraryService)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
        _viewModel.StartListening();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopListening();
    }

    /// <summary>
    /// Attach flow: either pick an already-uploaded file from the shared library, or pick a local
    /// file and upload it first — either way ends by calling <see cref="ChatViewModel.SetPendingAttachment"/>.
    /// Uses <c>DisplayActionSheet</c>/<c>FilePicker</c> here (not the ViewModel) per this codebase's
    /// established "native prompts live in the page code-behind" convention.
    /// </summary>
    private async void OnAttachClicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheetAsync("Attach a file", "Cancel", null, "Pick from Library", "Upload New File");
        if (choice is "Pick from Library")
        {
            await PickFromLibraryAsync();
        }
        else if (choice is "Upload New File")
        {
            await UploadAndAttachAsync();
        }
    }

    private async Task PickFromLibraryAsync()
    {
        IReadOnlyList<SharedLibraryFileSummary> results;
        try
        {
            results = await _libraryService.SearchAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Could not load the library", ex.Message, "OK");
            return;
        }

        if (results.Count == 0)
        {
            await DisplayAlertAsync("Shared library is empty", "Upload a file first, or use \"Upload New File\" instead.", "OK");
            return;
        }

        var labels = results.Select(r => string.IsNullOrWhiteSpace(r.FolderPath) ? r.FileName : $"{r.FolderPath}/{r.FileName}").ToArray();
        var choice = await DisplayActionSheetAsync("Pick a file", "Cancel", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;

        var picked = results[index];
        _viewModel.SetPendingAttachment(picked.Id, picked.FileName);
    }

    private async Task UploadAndAttachAsync()
    {
        FileResult? picked;
        try
        {
            picked = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Select a file to attach" });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Could not open the file picker", ex.Message, "OK");
            return;
        }

        if (picked is null) return;

        try
        {
            await using var stream = await picked.OpenReadAsync();
            var summary = await _libraryService.UploadAsync(string.Empty, picked.FileName, [], stream);
            _viewModel.SetPendingAttachment(summary.Id, summary.FileName);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync($"Could not upload '{picked.FileName}'", ex.Message, "OK");
        }
    }
}
