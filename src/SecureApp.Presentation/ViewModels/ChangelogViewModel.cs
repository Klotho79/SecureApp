using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Updates;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// "Co je nové" (2026-10-19, user's own ask: "Log do apky... jsem chtel" — the changelog should be
/// readable right in the app, not only as an external document). Purely offline: reads the bundled
/// <c>Resources/Raw/changelog.json</c> once per open — no relay call, nothing to fail over a flaky
/// connection.
/// </summary>
public sealed partial class ChangelogViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ObservableCollection<ChangelogDisplayItem> Releases { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    public ChangelogViewModel()
    {
        Releases = [];
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("changelog.json");
            var entries = await JsonSerializer.DeserializeAsync<List<ChangelogEntry>>(stream)
                ?? [];

            Releases = new ObservableCollection<ChangelogDisplayItem>(entries.Select(e => new ChangelogDisplayItem(
                $"Verze {e.Version} (build {e.Code}) — {e.Date}",
                string.Join("\n", e.Items.Select(i => "• " + i)))));
        }
        catch
        {
            // Best-effort — a missing/corrupt bundled resource must never crash the page, just show
            // nothing rather than a stack trace.
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public sealed record ChangelogDisplayItem(string Header, string BodyText);
