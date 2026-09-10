using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class LogbookManagePage : ContentPage
{
    private readonly LogbookManageViewModel _viewModel;

    public LogbookManagePage(LogbookManageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }

    /// <summary>Confirmation dialog lives here per this codebase's established convention — <see cref="LogbookManageViewModel.DeleteChecklistCommand"/> does the actual delete (local + relay) once confirmed.</summary>
    private async void OnDeleteChecklistClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: LogbookManageChecklistItem item }) return;

        var confirmed = await DisplayAlertAsync(
            "Smazat check-list",
            $"Smazat check-list „{item.Name}“? Zmizí u vás i u všech ostatních uživatelů.",
            "Smazat",
            "Storno");
        if (!confirmed) return;

        await _viewModel.DeleteChecklistCommand.ExecuteAsync(item.Id);
    }

    /// <summary>Same as <see cref="OnDeleteChecklistClicked"/> — the extra warning line matches <see cref="LogbookManageViewModel.DeleteProcedureTypeAsync"/>'s own remarks on the cascade.</summary>
    private async void OnDeleteProcedureTypeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: LogbookManageProcedureTypeItem item }) return;

        var confirmed = await DisplayAlertAsync(
            "Smazat typ výkonu",
            $"Smazat typ výkonu „{item.Name}“ ({item.Abbreviation})? Zmizí u vás i u všech ostatních uživatelů, a vaše vlastní dosud zaznamenané výkony tohoto typu se tím u vás také smažou ze statistiky.",
            "Smazat",
            "Storno");
        if (!confirmed) return;

        await _viewModel.DeleteProcedureTypeCommand.ExecuteAsync(item.Id);
    }
}
