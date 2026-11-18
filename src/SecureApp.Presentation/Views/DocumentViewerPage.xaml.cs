using System.ComponentModel;
using Microsoft.Maui.Dispatching;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class DocumentViewerPage : ContentPage
{
	private readonly DocumentViewerViewModel _viewModel;
	private ToolbarItem? _editItem;
	private ToolbarItem? _moreItem;

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
		ApplyLayout();
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
		if (e.PropertyName is nameof(DocumentViewerViewModel.IsCms) or nameof(DocumentViewerViewModel.IsPagedDocument))
			ApplyLayout();

		if (e.PropertyName == nameof(DocumentViewerViewModel.CmsModel) && _viewModel.CmsModel is not null)
			_ = ReloadAnnotationsAsync();

		if (e.PropertyName == nameof(DocumentViewerViewModel.CurrentPageImage))
			_zoom.Reset();
	}

	/// <summary>
	/// 2026-11-14 (user: "text je hnusny… maximálně přehledný"): a CMS document reads edge to edge
	/// (CmsDocumentView keeps its own 16-px gutter instead of 20 + 16), and on a phone the
	/// Přejmenovat/Stáhnout actions move into one "⋯" menu so the text gets the whole screen.
	/// 2026-11-16 (user: "když otevřeš soubor DOAC, je dokument miniaturní a tlačítka obrovská"): the same on a
	/// phone for every document type, not only CMS — a PDF page used to get 20 px of padding on each side and a
	/// row of four full-size buttons (⬅ Předchozí / Stránka x z y / ⬇ Stáhnout / Další ➡) that wrapped on a narrow
	/// screen and squeezed the page; now the page goes edge to edge and the pager is just ‹ Stránka x z y ›.
	/// </summary>
	private void ApplyLayout()
	{
		var desktop = DeviceInfo.Idiom == DeviceIdiom.Desktop;
		var cms = _viewModel.IsCms;

		// Zoom buttons are a PC affordance (phones pinch); shown only for CMS documents on desktop.
		ZoomBar.IsVisible = cms && desktop;

		RootGrid.Padding = desktop
			? (cms ? new Thickness(0, 0, 0, 12) : new Thickness(20))
			: new Thickness(0, 0, 0, 4);
		BottomBar.Margin = cms || !desktop ? new Thickness(16, 0) : new Thickness(0);

		// Phone: Stáhnout lives in the "⋯" menu; the bar stays only to page through a multi-page document.
		DownloadButton.IsVisible = desktop;
		BottomBar.IsVisible = desktop || _viewModel.IsPagedDocument && !cms;
		if (!desktop)
		{
			PreviousButton.Text = "‹";
			NextButton.Text = "›";
			foreach (var pager in new[] { PreviousButton, NextButton })
			{
				pager.FontSize = 22;
				pager.Padding = new Thickness(18, 0);
				pager.MinimumHeightRequest = 40;
			}
		}

		// "Upravit" opens the PC editor on this document (CMS_PLAN.md, phase 2a).
		if (_editItem is null)
		{
			_editItem = new ToolbarItem { Text = "📝 Upravit" };
			_editItem.Clicked += async (_, _) =>
				await Infrastructure.ShellNavigation.GoToAsync($"{nameof(CmsEditorPage)}?documentId={_viewModel.DocumentId}");
		}
		if (_moreItem is null)
		{
			_moreItem = new ToolbarItem { Text = "⋯" };
			_moreItem.Clicked += OnMoreClicked;
		}
		ToolbarItems.Remove(RenameItem);
		ToolbarItems.Remove(_moreItem);
		ToolbarItems.Remove(_editItem);
		ToolbarItems.Add(desktop ? RenameItem : _moreItem);
		if (cms && desktop) ToolbarItems.Add(_editItem);
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

	/// <summary>Phone CMS reader's "⋯" menu (2026-11-14): the same two actions as the toolbar/bottom bar elsewhere.</summary>
	private async void OnMoreClicked(object? sender, EventArgs e)
	{
		const string rename = "✏ Přejmenovat", download = "⬇ Stáhnout";
		var choice = await DisplayActionSheetAsync(_viewModel.Title, "Zrušit", null, _viewModel.CanDownload ? [rename, download] : [rename]);
		if (choice == rename) OnRenameClicked(sender, e);
		else if (choice == download) await _viewModel.DownloadCommand.ExecuteAsync(null);
	}

	/// <summary>2026-09-30, user's own ask — rename this document from the viewer itself.</summary>
	private async void OnRenameClicked(object? sender, EventArgs e)
	{
		var newName = await DisplayPromptAsync("Přejmenovat dokument", "Nový název:", initialValue: _viewModel.Title);
		if (string.IsNullOrWhiteSpace(newName)) return;

		await _viewModel.RenameDocumentCommand.ExecuteAsync(newName);
	}
}
