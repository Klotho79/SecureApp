using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SecureApp.Presentation.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		// 2026-10-23: a WinUI/XAML exception on the UI thread (layout, binding, a page opening) never
		// reaches AppDomain.UnhandledException before the process is torn down, so the PC build
		// crashed with no trace in any log ("Rozpis spadl", nothing reported). Log it here, and keep
		// the app alive: a failed page is better than losing the whole session.
		this.UnhandledException += (_, e) =>
		{
			SecureApp.Presentation.Infrastructure.AppLog.Critical("WinUI.UnhandledException", "Výjimka v uživatelském rozhraní (Windows).", e.Exception);
			e.Handled = true;
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

