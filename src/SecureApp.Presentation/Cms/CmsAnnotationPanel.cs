using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Cms;

/// <summary>
/// The list of annotations under a CMS document (CMS_PLAN.md, phase 4): who wrote what, on which quote,
/// shown to whom; reply / ignore / delete per entry. Pure presentation — the page owns the service calls
/// and re-feeds <see cref="SetItems"/> after each change.
/// </summary>
public sealed class CmsAnnotationPanel : ContentView
{
    private readonly Button _toggle = new() { HorizontalOptions = LayoutOptions.Start };
    private readonly Switch _showHidden = new();
    private readonly VerticalStackLayout _list = new() { Spacing = 10 };
    private readonly ScrollView _scroll;
    private readonly Border _body;
    private IReadOnlyList<CmsAnnotationView> _items = [];
    private bool _expanded;

    public event Action<CmsAnnotationView>? ReplyRequested;
    public event Action<CmsAnnotationView>? HideToggled;
    public event Action<CmsAnnotationView>? DeleteRequested;
    public event Action<CmsAnnotationView>? ProposeRequested;
    public event Action<CmsAnnotationView, string>? ModerateRequested;

    public CmsAnnotationPanel()
    {
        _toggle.Clicked += (_, _) => { _expanded = !_expanded; Refresh(); };
        _showHidden.Toggled += (_, _) => Refresh();

        var hiddenRow = new HorizontalStackLayout { Spacing = 6, Children = { _showHidden, new Label { Text = "Zobrazit ignorované", VerticalOptions = LayoutOptions.Center, FontSize = 12 } } };
        _scroll = new ScrollView { MaximumHeightRequest = 260, Content = _list };
        _body = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(12, 8),
            Content = new VerticalStackLayout { Spacing = 6, Children = { _toggle, hiddenRow, _scroll } }
        };
        _body.SetAppThemeColor(Border.BackgroundColorProperty, Colors.White, Color.FromArgb("#2A2A2E"));
        _body.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#CCCCCC"), Color.FromArgb("#555555"));
        Content = _body;
        Refresh();
    }

    public void SetItems(IReadOnlyList<CmsAnnotationView> items)
    {
        _items = items;
        Refresh();
    }

    private void Refresh()
    {
        var visible = _items.Where(a => _showHidden.IsToggled || !a.HiddenByMe).ToList();
        var hiddenCount = _items.Count(a => a.HiddenByMe);
        _toggle.Text = $"💬 Poznámky ({visible.Count}){(hiddenCount > 0 ? $" · ignorováno {hiddenCount}" : "")}  {(_expanded ? "▾" : "▸")}";
        _scroll.IsVisible = _expanded;
        ((HorizontalStackLayout)((VerticalStackLayout)_body.Content!).Children[1]).IsVisible = _expanded && hiddenCount > 0;
        if (!_expanded) return;

        _list.Children.Clear();
        if (visible.Count == 0)
        {
            _list.Children.Add(new Label { Text = "Zatím žádné poznámky. Označte text v dokumentu a zvolte „💬 Poznámka“.", FontSize = 12, TextColor = Colors.Gray });
            return;
        }
        foreach (var item in visible)
            _list.Children.Add(BuildItem(item));
    }

    private View BuildItem(CmsAnnotationView a)
    {
        var audience = a.Audience switch { "private" => "🔒 jen autor", "selected" => "👥 vybraní", _ => "🌐 všichni" };
        var stack = new VerticalStackLayout { Spacing = 3, Opacity = a.HiddenByMe ? 0.5 : 1 };
        var state = a.Moderation switch
        {
            "pending" => " · ⏳ navrženo k začlenění",
            "incorporated" => " · ✅ začleněno do dokumentu",
            "declined" => " · ✖ zamítnuto",
            "kept" => " · 📌 ponecháno jako komentář",
            _ => ""
        };
        stack.Children.Add(new Label { Text = $"{a.AuthorName} · {audience}{state} · {a.CreatedAtUtc.ToLocalTime():d.M. H:mm}", FontSize = 11, TextColor = Colors.Gray });
        if (a.Anchor is not null)
            stack.Children.Add(new Label { Text = $"„{a.Anchor.Exact}“", FontSize = 12, FontAttributes = FontAttributes.Italic, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 });
        stack.Children.Add(new Label { Text = a.Text, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap });

        foreach (var reply in a.Replies)
        {
            var box = new VerticalStackLayout { Margin = new Thickness(14, 0, 0, 0), Spacing = 0 };
            box.Children.Add(new Label { Text = $"{reply.AuthorName} · {reply.CreatedAtUtc.ToLocalTime():d.M. H:mm}", FontSize = 10, TextColor = Colors.Gray });
            box.Children.Add(new Label { Text = reply.Text, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap });
            stack.Children.Add(box);
        }

        var actions = new HorizontalStackLayout { Spacing = 6 };
        actions.Children.Add(MakeButton("Odpovědět", () => ReplyRequested?.Invoke(a)));
        actions.Children.Add(MakeButton(a.HiddenByMe ? "Zobrazit" : "Ignorovat", () => HideToggled?.Invoke(a)));
        if (a.IsMine && a.Moderation is "none" or "declined") actions.Children.Add(MakeButton("Navrhnout do dokumentu", () => ProposeRequested?.Invoke(a)));
        // "Začlenit" needs the editor (ruční úprava bloku) — only in the shared queue (CmsAnnotationQueuePage), not here.
        if (a.CanModerate && a.Moderation == "pending" && !a.IsMine)
        {
            actions.Children.Add(MakeButton("Ponechat", () => ModerateRequested?.Invoke(a, "kept")));
            actions.Children.Add(MakeButton("Odmítnout", () => ModerateRequested?.Invoke(a, "declined")));
        }
        if (a.IsMine) actions.Children.Add(MakeButton("Smazat", () => DeleteRequested?.Invoke(a)));
        stack.Children.Add(actions);
        return stack;
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var button = new Button { Text = text, FontSize = 11, Padding = new Thickness(8, 2), MinimumHeightRequest = 28, HeightRequest = 30 };
        button.Clicked += (_, _) => onClick();
        return button;
    }
}
