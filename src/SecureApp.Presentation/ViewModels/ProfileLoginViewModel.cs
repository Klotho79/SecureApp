using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Profiles;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Windows-only profile picker/login (2026-10-05, see <see cref="ActiveProfile"/>'s own remarks) —
/// shown instead of <see cref="Views.AppShell"/> while <see cref="ActiveProfile.RequiresLogin"/> is
/// true. No DI: <see cref="ProfileRegistry"/>/<see cref="ActiveProfile"/> are static, file-backed, and
/// deliberately readable before any of the app's real (data-directory-dependent) services exist.
/// </summary>
public sealed partial class ProfileLoginViewModel : ObservableObject
{
    public ObservableCollection<ProfileRegistry.Profile> Profiles { get; } = new(ProfileRegistry.LoadAll());

    public bool HasProfiles => Profiles.Count > 0;

    public bool HasNoProfiles => Profiles.Count == 0;

    [ObservableProperty]
    public partial ProfileRegistry.Profile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string PasswordText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCreatingNewProfile { get; set; }

    /// <summary>Drives the existing-profile picker's own visibility — this codebase's established "no converters" convention (a plain inverse bool property, same pattern as e.g. ContactsViewModel.HasNoResults) rather than an IsCreatingNewProfile-inverting converter.</summary>
    public bool IsSelectingExistingProfile => !IsCreatingNewProfile;

    partial void OnIsCreatingNewProfileChanged(bool value) => OnPropertyChanged(nameof(IsSelectingExistingProfile));

    [ObservableProperty]
    public partial string NewProfileNameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPasswordText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPasswordConfirmText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnSelectedProfileChanged(ProfileRegistry.Profile? value) => ErrorText = null;

    [RelayCommand]
    private void ShowCreateNewProfile()
    {
        IsCreatingNewProfile = true;
        ErrorText = null;
    }

    [RelayCommand]
    private void CancelCreateNewProfile()
    {
        IsCreatingNewProfile = false;
        NewProfileNameText = string.Empty;
        NewPasswordText = string.Empty;
        NewPasswordConfirmText = string.Empty;
        ErrorText = null;
    }

    /// <summary>Verifies the chosen existing profile's password and, if it matches, hands off to <see cref="ActiveProfile.SetActiveAndRestart"/> — this never returns on success (the process restarts).</summary>
    [RelayCommand]
    private void LogIn()
    {
        if (SelectedProfile is not { } profile)
        {
            ErrorText = "Nejprve vyberte svůj profil.";
            return;
        }
        if (!ProfileRegistry.TryVerifyPassword(profile, PasswordText))
        {
            ErrorText = "Špatné heslo.";
            return;
        }
        ActiveProfile.SetActiveAndRestart(profile);
    }

    /// <summary>Creates a brand-new profile and immediately logs into it — never returns on success.</summary>
    [RelayCommand]
    private void CreateAndLogIn()
    {
        var name = NewProfileNameText.Trim();
        if (name.Length == 0)
        {
            ErrorText = "Zadejte jméno nového profilu.";
            return;
        }
        if (Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText = "Profil s tímto jménem už existuje.";
            return;
        }
        if (NewPasswordText.Length < 4)
        {
            ErrorText = "Heslo musí mít alespoň 4 znaky.";
            return;
        }
        if (NewPasswordText != NewPasswordConfirmText)
        {
            ErrorText = "Hesla se neshodují.";
            return;
        }

        var profile = ProfileRegistry.Create(name, NewPasswordText);
        ActiveProfile.SetActiveAndRestart(profile);
    }
}
