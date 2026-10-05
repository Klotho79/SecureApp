using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>Windows-only profile picker/login — see <c>ViewModels.ProfileLoginViewModel</c>'s own remarks. No DI: constructed directly by <c>App.CreateWindow</c> before the real DI-backed app exists.</summary>
public partial class ProfileLoginPage : ContentPage
{
    public ProfileLoginPage()
    {
        InitializeComponent();
        BindingContext = new ProfileLoginViewModel();
    }
}
