using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class MurrayScoreCalculatorPage : ContentPage
{
    public MurrayScoreCalculatorPage(MurrayScoreCalculatorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
