using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class DocumentViewerPage : ContentPage
{
	private readonly DocumentViewerViewModel _viewModel;

	public DocumentViewerPage(DocumentViewerViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _viewModel = viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadDocumentCommand.Execute(null);
		_viewModel.StartWatermark();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_viewModel.StopWatermark();
	}
}
