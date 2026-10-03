using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class GcsCalculatorPage : ContentPage
{
    public GcsCalculatorPage(GcsCalculatorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
