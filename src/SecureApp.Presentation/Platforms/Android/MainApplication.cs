using Android.App;
using Android.Runtime;
using SecureApp.Presentation.Platforms.Android;

namespace SecureApp.Presentation;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override void OnCreate()
	{
		base.OnCreate();

		// Phase 7 home-screen widget (2026-09-22) — subscribed once here rather than from the
		// widget provider's own static ctor: Application.OnCreate is guaranteed to run before ANY
		// other Android component (including a BroadcastReceiver like NotificationsWidgetProvider)
		// gets a chance to fire, and base.OnCreate() above is what actually builds the MauiApp/DI
		// container this subscription (and the provider's own DI resolution) both depend on.
		NotificationsWidgetProvider.SubscribeToChanges();
	}
}
