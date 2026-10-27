using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Redakce as a menu (2026-10-26, user: "v redakci budou jen tlačítka otevírající okna… název + tlačítko je nesmysl,
/// tlačítko s příslušným názvem je přehlednější"). The tab shows one button per action; the cards that used to sit
/// inline open in <see cref="Views.RedakceSectionPage"/>, which uses this same ViewModel with <see cref="Section"/> set.
/// </summary>
public sealed partial class RedakceViewModel
{
    public const string BoardSection = "board";
    public const string RoomStatusSection = "roomstatus";
    public const string RoomNotesSection = "roomnotes";
    public const string MyAssignmentsSection = "myassignments";
    public const string CmsAssignSection = "cmsassign";
    public const string LibraryCategoriesSection = "library";

    /// <summary>Which card <see cref="Views.RedakceSectionPage"/> shows; null on the menu itself.</summary>
    [ObservableProperty]
    public partial string? Section { get; set; }

    public bool IsBoardSection => Section == BoardSection;
    public bool IsRoomStatusSection => Section == RoomStatusSection;
    public bool IsRoomNotesSection => Section == RoomNotesSection;
    public bool IsMyAssignmentsSection => Section == MyAssignmentsSection;
    public bool IsCmsAssignSection => Section == CmsAssignSection;
    public bool IsLibraryCategoriesSection => Section == LibraryCategoriesSection;

    public string SectionTitle => Section switch
    {
        BoardSection => "Zpráva na nástěnku",
        RoomStatusSection => "Možné stavy sálů",
        RoomNotesSection => "Možné poznámky k sálům",
        MyAssignmentsSection => "Moje zadání",
        CmsAssignSection => "Zadání dokumentů",
        LibraryCategoriesSection => "Kategorie knihovny",
        _ => "Redakce",
    };

    partial void OnSectionChanged(string? value)
    {
        OnPropertyChanged(nameof(IsBoardSection));
        OnPropertyChanged(nameof(IsRoomStatusSection));
        OnPropertyChanged(nameof(IsRoomNotesSection));
        OnPropertyChanged(nameof(IsMyAssignmentsSection));
        OnPropertyChanged(nameof(IsCmsAssignSection));
        OnPropertyChanged(nameof(IsLibraryCategoriesSection));
        OnPropertyChanged(nameof(SectionTitle));
    }

    /// <summary>Admin/Modifier — the overview of documents still being written or waiting for review, with a preview.</summary>
    [ObservableProperty]
    public partial bool CanSeeInProgressDocuments { get; set; }

    [RelayCommand]
    private void OpenInProgressDocuments() => RequestNavigate?.Invoke(nameof(Views.InProgressDocumentsPage));

    [RelayCommand]
    private void OpenSection(string section) => RequestNavigate?.Invoke($"{nameof(Views.RedakceSectionPage)}?section={section}");
}
