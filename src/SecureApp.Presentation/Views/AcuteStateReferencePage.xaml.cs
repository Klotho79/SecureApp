using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class AcuteStateReferencePage : ContentPage
{
    public AcuteStateReferencePage(AcuteStateReferenceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
