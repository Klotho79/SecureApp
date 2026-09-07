using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using SecureApp.Data;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Library;
using SecureApp.Presentation.Rendering;
using SecureApp.Presentation.Transport;
using SecureApp.Presentation.ViewModels;
using SecureApp.Presentation.Views;
using SkiaSharp.Views.Maui.Controls.Hosting;
using ZXing.Net.Maui.Controls;

namespace SecureApp.Presentation;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseSkiaSharp() // Registers SkiaSharp/HarfBuzz rendering handlers for the in-app document/image viewer.
			.UseBarcodeReader() // ZXing.Net.MAUI — QR generate (BarcodeGeneratorView) + camera scan (CameraBarcodeReaderView) for chat pairing.
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				// 2026-09-06 visual redesign — a serif for headings/document titles (reads as
				// clinical/editorial authority) paired with a variable-weight sans for everything
				// else. IBM Plex Sans no longer ships static per-weight files on Google Fonts (the
				// whole repo has moved to variable fonts) — the single variable file responds to
				// FontAttributes/requested weight fine on all 4 targets since each platform's own
				// modern text stack (DirectWrite/CoreText/Android's renderer) resolves the wght axis.
				fonts.AddFont("Spectral-Medium.ttf", "SpectralMedium");
				fonts.AddFont("Spectral-SemiBold.ttf", "SpectralSemibold");
				fonts.AddFont("Spectral-Bold.ttf", "SpectralBold");
				fonts.AddFont("IBMPlexSans-Variable.ttf", "PlexSans");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Manual multi-"device" testing convenience (Milestone 5 chat, no relay deployed
		// anywhere yet): set SECUREAPP_DATA_DIR before launching the built .exe to run two
		// isolated identities side-by-side on one machine (e.g. $env:SECUREAPP_DATA_DIR =
		// "C:\Temp\SecureApp-Alice"; .\SecureApp.Presentation.exe, then again with "...-Bob"
		// in a second window) — separate SQLite DB *and* separate vault keys (see
		// MauiSecureVaultKeyStore's keyPrefix remarks), so the two never collide. Empty/unset
		// in the normal single-identity case, which is still the only thing any real device
		// needs.
		var dataDirOverride = Environment.GetEnvironmentVariable("SECUREAPP_DATA_DIR");
		var vaultKeyPrefix = string.IsNullOrWhiteSpace(dataDirOverride) ? "" : "test:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDirOverride)))[..12];

		// --- Core infrastructure registration (Step 2) ---
		// The Data layer never touches MAUI platform APIs directly; the app-data
		// directory is resolved here (Presentation) and handed down as configuration.
		//
		// IMPORTANT: passed as a factory, not a pre-built DataStorageOptions. Calling
		// FileSystem.AppDataDirectory eagerly *here* (before builder.Build() returns)
		// touches WinRT/COM before the Windows UI thread's apartment is initialized,
		// which silently terminates the app on startup with no exception/event-log entry.
		// The factory defers it until something actually resolves DataStorageOptions,
		// which only happens after the app has launched.
		builder.Services.AddDataInfrastructure(() =>
			new DataStorageOptions(string.IsNullOrWhiteSpace(dataDirOverride) ? FileSystem.AppDataDirectory : dataDirOverride));

		// ISecureVaultKeyStore lives here rather than in SecureApp.Data because it needs
		// Microsoft.Maui.Storage.ISecureStorage (Android Keystore / iOS+macOS Keychain /
		// Windows DPAPI-Credential Locker), which the platform-agnostic Data class library
		// cannot reference. BouncyCastleCryptoService (registered in AddDataInfrastructure
		// above) depends on this interface, resolved here regardless of which layer's
		// registration call runs first.
		builder.Services.AddSingleton(SecureStorage.Default);
		builder.Services.AddSingleton<ISecureVaultKeyStore>(sp => new MauiSecureVaultKeyStore(sp.GetRequiredService<ISecureStorage>(), vaultKeyPrefix));

		// IDocumentRenderingService lives here for the same reason — it needs SkiaSharp/PDFtoImage,
		// which the platform-agnostic Data class library cannot reference. It does pull
		// IDocumentRepository/ICryptoService/ISpreadsheetParsingService from Data's own
		// registrations above, just like the other direction works for ISecureVaultKeyStore.
		builder.Services.AddScoped<IDocumentRenderingService, DocumentRenderingService>();

		// Milestone 4 (DLP): platform-specific screen-capture prevention. Lives under
		// Platforms/<Platform>/NativeDlpService.cs for the same reason ISecureVaultKeyStore/
		// IDocumentRenderingService do — needs APIs this platform-agnostic-by-design app
		// otherwise avoids referencing outside Presentation. Applied once at startup, see App.xaml.cs.
		builder.Services.AddSingleton<INativeDlpService, NativeDlpService>();

		// Milestone 5 (E2EE Chat) transport: relay server + client (see DEVELOPMENT_PLAN.md's note).
		// Lives here rather than SecureApp.Data for the same reason as everything else in this
		// block — WebSocketMessageTransport is actually platform-agnostic BCL code, but it's the
		// Presentation layer's job to supply *an* IMessageTransport implementation, same division
		// of responsibility as ISecureVaultKeyStore. Auto-connect-at-startup is wired in
		// App.xaml.cs's CreateWindow, same place/pattern as the DLP service below.
		builder.Services.AddSingleton<IMessageTransport, WebSocketMessageTransport>();

		// Shared community file library, hosted on the relay (see DEVELOPMENT_PLAN.md's note) —
		// same registration shape as IMessageTransport above, same reasoning.
		builder.Services.AddSingleton<ISharedLibraryService, HttpSharedLibraryService>();

		// Lets an Admin-role device mint relay invite codes in-app instead of SSH+curl on the Pi.
		builder.Services.AddSingleton<IRelayAdminService, HttpRelayAdminService>();

		// Member directory (2026-09-06) — same registration shape as ISharedLibraryService above.
		builder.Services.AddSingleton<IContactDirectoryService, HttpContactDirectoryService>();

		// --- Milestone 3: Presentation (pages + view models) ---
		builder.Services.AddTransient<DocumentBrowserViewModel>();
		builder.Services.AddTransient<DocumentBrowserPage>();
		builder.Services.AddTransient<DocumentViewerViewModel>();
		builder.Services.AddTransient<DocumentViewerPage>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<SettingsPage>();

		// Milestone 5 (E2EE Chat) UI.
		builder.Services.AddTransient<ChatListViewModel>();
		builder.Services.AddTransient<ChatListPage>();
		builder.Services.AddTransient<NewChatViewModel>();
		builder.Services.AddTransient<NewChatPage>();

		// Group chats (2026-09-07) — see GroupChat's own remarks for the design.
		builder.Services.AddTransient<NewGroupViewModel>();
		builder.Services.AddTransient<NewGroupPage>();
		builder.Services.AddTransient<GroupChatViewModel>();
		builder.Services.AddTransient<GroupChatPage>();
		builder.Services.AddTransient<ChatViewModel>();
		builder.Services.AddTransient<ChatPage>();

		// Shared community file library.
		builder.Services.AddTransient<LibraryViewModel>();
		builder.Services.AddTransient<LibraryPage>();

		return builder.Build();
	}
}
