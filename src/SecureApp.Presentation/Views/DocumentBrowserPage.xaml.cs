using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class DocumentBrowserPage : ContentPage
{
	private readonly DocumentBrowserViewModel _viewModel;

	public DocumentBrowserPage(DocumentBrowserViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _viewModel = viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadCommand.Execute(null);
	}

	private void OnFolderSelected(object? sender, SelectionChangedEventArgs e)
	{
		FoldersView.SelectedItem = null;
		if (e.CurrentSelection.FirstOrDefault() is DocumentFolderItem folder)
			_viewModel.OpenFolderCommand.Execute(folder);
	}

	private void OnDocumentSelected(object? sender, SelectionChangedEventArgs e)
	{
		DocumentsView.SelectedItem = null;
		if (e.CurrentSelection.FirstOrDefault() is DocumentItem document)
			_viewModel.OpenDocumentCommand.Execute(document);
	}

	private async void OnNewFolderClicked(object? sender, EventArgs e)
	{
		var name = await DisplayPromptAsync("Nová složka", "Název složky:");
		if (string.IsNullOrWhiteSpace(name)) return;

		await _viewModel.CreateFolderCommand.ExecuteAsync(name);
	}

	private async void OnRenameFolderClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { BindingContext: DocumentFolderItem folder }) return;

		var newName = await DisplayPromptAsync("Přejmenovat složku", "Nový název:", initialValue: folder.Name);
		if (string.IsNullOrWhiteSpace(newName)) return;

		await _viewModel.RenameFolderCommand.ExecuteAsync((folder, newName));
	}

	private async void OnDeleteFolderClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { BindingContext: DocumentFolderItem folder }) return;

		var confirmed = await DisplayAlertAsync(
			"Smazat složku",
			$"Smazat složku '{folder.Name}'? Podsložky uvnitř ní budou smazány také. Dokumenty přímo v ní se místo smazání přesunou do kořenové úrovně.",
			"Smazat",
			"Zrušit");
		if (!confirmed) return;

		await _viewModel.DeleteFolderCommand.ExecuteAsync(folder);
	}
}
