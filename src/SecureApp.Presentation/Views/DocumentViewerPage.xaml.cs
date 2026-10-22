using System.ComponentModel;
using Microsoft.Maui.Dispatching;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class DocumentViewerPage : ContentPage
{
	private readonly DocumentViewerViewModel _viewModel;
	private ToolbarItem? _editItem;

	// Pinch-to-zoom + one-finger pan + double-tap reset on the page image: shared with the CMS image
	// viewer (Controls/PinchZoomController holds the tuned gesture math and its history).
	private readonly SecureApp.Presentation.Controls.PinchZoomController _zoom;

	private readonly SecureApp.Domain.Interfaces.Services.ICmsAnnotationService _annotations;
	private readonly SecureApp.Domain.Interfaces.Services.IContactDirectoryService _directory;

	public DocumentViewerPage(
		DocumentViewerViewModel viewModel, SecureApp.Presentation.Cms.ICmsMediaProvider cmsMediaProvider,
		SecureApp.Domain.Interfaces.Services.ICmsAnnotationService annotations,
		SecureApp.Domain.Interfaces.Services.IContactDirectoryService directory)
	{
		InitializeComponent();
		_zoom = new SecureApp.Presentation.Controls.PinchZoomController(DocumentImage);
		BindingContext = _viewModel = viewModel;
		_viewModel.PropertyChanged += OnViewModelPropertyChanged;
		CmsView.MediaProvider = cmsMediaProvider;
		_annotations = annotations;
		_directory = directory;
		AnnotationPanel.ReplyRequested += OnAnnotationReply;
		AnnotationPanel.HideToggled += OnAnnotationHideToggled;
		AnnotationPanel.DeleteRequested += OnAnnotationDelete;
		AnnotationPanel.ProposeRequested += OnAnnotationPropose;
		AnnotationPanel.ModerateRequested += OnAnnotationModerate;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		// Defer the (heavy) image render until the open animation has settled, so rasterizing the
		// image doesn't jank the slide-in (2026-09-11) — the render itself also runs off the UI
		// thread now (see DocumentViewerViewModel.GoToPageAsync). Same "separate graphics from
		// loading" principle as the chat pages.
		Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(280), () => _viewModel.LoadDocumentCommand.Execute(null));
	}

	/// <summary>A fresh page always starts unzoomed — a leftover zoom/pan from the previous page would land the new page's content half off-screen.</summary>
	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// Zoom buttons are a PC affordance (phones pinch); shown only for CMS documents on desktop.
		if (e.PropertyName == nameof(DocumentViewerViewModel.IsCms))
		{
			var desktop = DeviceInfo.Idiom == DeviceIdiom.Desktop;
			ZoomBar.IsVisible = _viewModel.IsCms && desktop;

			// "Upravit" opens the PC editor on this document (CMS_PLAN.md, phase 2a).
			if (_editItem is null)
			{
				_editItem = new ToolbarItem { Text = "📝 Upravit" };
				_editItem.Clicked += async (_, _) =>
					await Infrastructure.ShellNavigation.GoToAsync($"{nameof(CmsEditorPage)}?documentId={_viewModel.DocumentId}");
			}
			ToolbarItems.Remove(_editItem);
			if (_viewModel.IsCms && desktop) ToolbarItems.Add(_editItem);
		}

		if (e.PropertyName == nameof(DocumentViewerViewModel.CmsModel) && _viewModel.CmsModel is not null)
			_ = ReloadAnnotationsAsync();

		if (e.PropertyName == nameof(DocumentViewerViewModel.CurrentPageImage))
			_zoom.Reset();
	}

	// --- Annotations (CMS_PLAN.md, phase 4) ---

	private async Task ReloadAnnotationsAsync()
	{
		if (_viewModel.CmsModel is not { } document) return;
		try
		{
			var items = await _annotations.ListAsync(document.DocumentId);
			AnnotationPanel.SetItems(items);
			CmsView.SetAnnotationMarks(items.Where(a => !a.HiddenByMe && a.Anchor is not null).Select(a => a.Anchor!));
		}
		catch (Exception ex)
		{
			AppLog.Error("CmsAnnotations.List", "load failed", ex); // the document itself must stay readable offline
		}
	}

	/// <summary>Full-screen pinch-zoom for a tapped CMS inline image (CMS_PLAN.md, phase 3).</summary>
	private async void OnCmsImageTapped(object? sender, string path) =>
		await Infrastructure.ShellNavigation.GoToAsync($"{nameof(CmsImageViewerPage)}?path={Uri.EscapeDataString(path)}");

	private async void OnAnnotationRequested(object? sender, SecureApp.Domain.ValueObjects.CmsAnchor anchor)
	{
		if (_viewModel.CmsModel is not { } document) return;
		try
		{
			var text = await DisplayPromptAsync("Poznámka", $"„{anchor.Exact}“", "Uložit", "Zrušit", maxLength: 2000, keyboard: Keyboard.Text);
			if (string.IsNullOrWhiteSpace(text)) return;

			var choice = await DisplayActionSheetAsync("Kdo poznámku uvidí?", "Zrušit", null, "Všichni", "Vybraní členové", "Jen já");
			if (choice is null or "Zrušit") return;

			var audience = choice switch { "Všichni" => "all", "Vybraní členové" => "selected", _ => "private" };
			var recipients = new List<Guid>();
			if (audience == "selected")
			{
				recipients = await PickRecipientsAsync();
				if (recipients.Count == 0) return;
			}

			await _annotations.CreateAsync(document.DocumentId, anchor, text, audience, recipients);
			CmsView.ClearSelection();
			await ReloadAnnotationsAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Poznámka", $"Uložení se nezdařilo: {ex.Message}", "OK");
		}
	}

	/// <summary>Picks members one at a time (an action sheet has no multi-select); each member may own several devices, all of which are sent.</summary>
	private async Task<List<Guid>> PickRecipientsAsync()
	{
		var members = (await _directory.ListMembersAsync())
			.GroupBy(m => m.DisplayName)
			.ToDictionary(g => g.Key, g => g.Select(m => m.RelayDeviceId).ToList());
		var picked = new List<string>();
		while (true)
		{
			var remaining = members.Keys.Except(picked).OrderBy(n => n).ToArray();
			if (remaining.Length == 0) break;
			var title = picked.Count == 0 ? "Komu poznámku ukázat?" : $"Vybráno: {string.Join(", ", picked)}";
			var name = await DisplayActionSheetAsync(title, "Zrušit", picked.Count > 0 ? "Hotovo" : null, remaining);
			if (name is null or "Zrušit") { if (picked.Count == 0) return []; break; }
			if (name == "Hotovo") break;
			picked.Add(name);
		}
		return picked.SelectMany(n => members[n]).ToList();
	}

	private async void OnAnnotationReply(SecureApp.Domain.Interfaces.Services.CmsAnnotationView annotation)
	{
		var text = await DisplayPromptAsync("Odpověď", annotation.Anchor is null ? annotation.Text : $"„{annotation.Anchor.Exact}“", "Odeslat", "Zrušit", maxLength: 2000, keyboard: Keyboard.Text);
		if (string.IsNullOrWhiteSpace(text)) return;
		try
		{
			await _annotations.ReplyAsync(annotation.Id, text);
			await ReloadAnnotationsAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Odpověď", $"Odeslání se nezdařilo: {ex.Message}", "OK");
		}
	}

	private async void OnAnnotationHideToggled(SecureApp.Domain.Interfaces.Services.CmsAnnotationView annotation)
	{
		try
		{
			await _annotations.SetHiddenAsync(annotation.Id, !annotation.HiddenByMe);
			await ReloadAnnotationsAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Poznámka", ex.Message, "OK");
		}
	}

	private async void OnAnnotationPropose(SecureApp.Domain.Interfaces.Services.CmsAnnotationView annotation)
	{
		if (!await DisplayAlertAsync("Navrhnout do dokumentu", "Redaktor dokumentu uvidí vaši poznámku a rozhodne, jestli ji začlení do textu. Navrhnout?", "Navrhnout", "Zrušit")) return;
		try
		{
			await _annotations.ProposeAsync(annotation.Id, null);
			await ReloadAnnotationsAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Poznámka", ex.Message, "OK");
		}
	}

	private async void OnAnnotationModerate(SecureApp.Domain.Interfaces.Services.CmsAnnotationView annotation, string decision)
	{
		var question = decision switch
		{
			"kept" => "Ponechat poznámku jako komentář viditelný všem?",
			_ => "Zamítnout návrh?"
		};
		if (!await DisplayAlertAsync("Moderace poznámky", question, "Ano", "Zrušit")) return;
		try
		{
			await _annotations.ModerateAsync(annotation.Id, decision, null);
			await ReloadAnnotationsAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Poznámka", ex.Message, "OK");
		}
	}

	private async void OnAnnotationDelete(SecureApp.Domain.Interfaces.Services.CmsAnnotationView annotation)
	{
		if (!await DisplayAlertAsync("Smazat poznámku", "Poznámka zmizí všem, kdo ji vidí. Smazat?", "Smazat", "Zrušit")) return;
		try
		{
			await _annotations.DeleteAsync(annotation.Id);
			await ReloadAnnotationsAsync();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Poznámka", ex.Message, "OK");
		}
	}

	/// <summary>2026-09-30, user's own ask — rename this document from the viewer itself.</summary>
	private async void OnRenameClicked(object? sender, EventArgs e)
	{
		var newName = await DisplayPromptAsync("Přejmenovat dokument", "Nový název:", initialValue: _viewModel.Title);
		if (string.IsNullOrWhiteSpace(newName)) return;

		await _viewModel.RenameDocumentCommand.ExecuteAsync(newName);
	}
}
