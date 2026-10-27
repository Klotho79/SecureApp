using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

/// <summary>One Redakce action in its own window (2026-10-26) — see RedakceViewModel.Sections.cs. Query: <c>section</c>.</summary>
public partial class RedakceSectionPage : ContentPage, IQueryAttributable
{
    private readonly RedakceViewModel _viewModel;

    public RedakceSectionPage(RedakceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.RequestNavigate += OnRequestNavigate;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("section", out var section))
            _viewModel.Section = section?.ToString();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    private async void OnRequestNavigate(string route) => await Infrastructure.ShellNavigation.GoToAsync(route);
}
