using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Managing the notice board's existing posts (2026-11-06, user: "je nutne aby byla moznost tech moderu a adminu
/// aby mohl upravit smazat zpravu na nastence"): the Redakce "Zpráva na nástěnku" card lists the latest posts, each
/// with Upravit (loads the text into the composer) and Smazat. The relay enforces Admin/Modifier for both.
/// </summary>
public sealed partial class RedakceViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<BoardPostItem> BoardPosts { get; set; } = [];

    [ObservableProperty]
    public partial bool HasBoardPosts { get; set; }

    /// <summary>Set while the composer holds the text of an existing post; "Odeslat" then saves the change instead of posting a new one.</summary>
    [ObservableProperty]
    public partial Guid? EditingBoardPostId { get; set; }

    public bool IsEditingBoardPost => EditingBoardPostId is not null;

    public string BoardSendButtonText => IsEditingBoardPost ? "Uložit změnu" : "Odeslat na nástěnku";

    partial void OnEditingBoardPostIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(IsEditingBoardPost));
        OnPropertyChanged(nameof(BoardSendButtonText));
    }

    private async Task RefreshBoardPostsAsync()
    {
        try
        {
            var posts = await _communityBoardService.ListAsync(30);
            BoardPosts = new ObservableCollection<BoardPostItem>(posts.Select(p => new BoardPostItem(
                p.Id,
                p.AuthorDisplayName,
                p.Text,
                p.CreatedAtUtc.ToLocalTime().ToString("d. M. yyyy HH:mm", CultureInfo.CurrentCulture),
                p.IsReadable)));
        }
        catch
        {
            BoardPosts = [];
        }

        HasBoardPosts = BoardPosts.Count > 0;
    }

    [RelayCommand]
    private void EditBoardPost(BoardPostItem? item)
    {
        if (item is null) return;
        if (!item.IsReadable)
        {
            BoardStatusText = "Tento příspěvek nejde upravit, protože ho toto zařízení nemůže přečíst (chybí klíč sdílené knihovny).";
            return;
        }

        EditingBoardPostId = item.Id;
        BoardMessageText = item.Text;
        BoardStatusText = null;
    }

    [RelayCommand]
    private void CancelBoardEdit()
    {
        EditingBoardPostId = null;
        BoardMessageText = string.Empty;
        BoardStatusText = null;
    }

    [RelayCommand]
    private async Task DeleteBoardPostAsync(BoardPostItem? item)
    {
        if (item is null) return;

        var confirmed = await Shell.Current.DisplayAlertAsync("Smazat příspěvek", "Opravdu smazat tento příspěvek z nástěnky?", "Smazat", "Zrušit");
        if (!confirmed) return;

        try
        {
            if (!await _communityBoardService.DeleteAsync(item.Id))
            {
                BoardStatusText = "Příspěvek se nepodařilo smazat (možná už neexistuje).";
                await RefreshBoardPostsAsync();
                return;
            }

            if (EditingBoardPostId == item.Id) CancelBoardEdit();
            BoardStatusText = "Příspěvek smazán.";
            await RefreshBoardPostsAsync();
        }
        catch (Exception ex)
        {
            BoardStatusText = $"Nepodařilo se smazat: {ex.Message}";
        }
    }
}

/// <summary>One existing notice-board post in the Redakce list.</summary>
public sealed record BoardPostItem(Guid Id, string Author, string Text, string WhenText, bool IsReadable);
