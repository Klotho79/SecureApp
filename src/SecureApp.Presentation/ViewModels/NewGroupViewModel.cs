using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Chat;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Creates a new group chat: name it, pick members from the relay's directory (same
/// <see cref="IContactDirectoryService"/> "New Chat" already uses — see <see cref="GroupChat"/>'s
/// own remarks for why a group is really a full mesh of ordinary pairwise sessions, not a new
/// group ratchet), and the founder's device establishes/pairs with each selected member and
/// broadcasts the group's membership snapshot to all of them. Reuses
/// <see cref="NewChatViewModel"/>'s exact pairing/auto-delivery pattern per member rather than
/// sharing code with it — a group create is a one-time, N-member fan-out of what that view model
/// already does for one peer, and duplicating stays easier to follow than threading a generic
/// "for each peer" abstraction through both.
/// </summary>
public sealed partial class NewGroupViewModel : ObservableObject
{
    private readonly IContactDirectoryService _contactDirectoryService;
    private readonly IMessagingService _messagingService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly IMessageTransport _messageTransport;
    private readonly IGroupChatRepository _groupChatRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IDiagnosticsReporter _diagnosticsReporter;

    [ObservableProperty]
    public partial string GroupNameText { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SelectableMemberItem> Members { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMembers { get; set; }

    [ObservableProperty]
    public partial bool HasNoMembers { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    [ObservableProperty]
    public partial Guid? CreatedGroupId { get; set; }

    [ObservableProperty]
    public partial bool HasCreatedGroup { get; set; }

    public NewGroupViewModel(
        IContactDirectoryService contactDirectoryService,
        IMessagingService messagingService,
        ICurrentUserService currentUserService,
        ITransportSettingsRepository transportSettingsRepository,
        IMessageTransport messageTransport,
        IGroupChatRepository groupChatRepository,
        IGroupMemberRepository groupMemberRepository,
        IDiagnosticsReporter diagnosticsReporter)
    {
        _contactDirectoryService = contactDirectoryService ?? throw new ArgumentNullException(nameof(contactDirectoryService));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _messageTransport = messageTransport ?? throw new ArgumentNullException(nameof(messageTransport));
        _groupChatRepository = groupChatRepository ?? throw new ArgumentNullException(nameof(groupChatRepository));
        _groupMemberRepository = groupMemberRepository ?? throw new ArgumentNullException(nameof(groupMemberRepository));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));

        GroupNameText = string.Empty;
        Members = [];
        HasNoMembers = true;
    }

    partial void OnStatusErrorMessageChanged(string? value)
    {
        HasStatusError = !string.IsNullOrEmpty(value);
        if (HasStatusError) _ = _diagnosticsReporter.ReportAsync(DiagnosticLogLevel.Error, value!, nameof(NewGroupViewModel));
    }

    partial void OnCreatedGroupIdChanged(Guid? value) => HasCreatedGroup = value is not null;

    /// <summary>
    /// Best-effort reconnect before listing, same reasoning as <c>ChatViewModel.EnsureConnectedAsync</c>
    /// — picking members to invite needs a live relay connection just as much as sending a message
    /// does, so this page shouldn't show an empty list just because the connection dropped since the
    /// app last background-reconnected.
    /// </summary>
    private async Task EnsureConnectedAsync()
    {
        if (_messageTransport.IsConnected) return;
        try
        {
            var configuration = await _transportSettingsRepository.GetAsync();
            if (configuration is { AssignedDeviceId: not null, IsAutoConnectEnabled: true, EndpointUri: { } endpoint })
                await _messageTransport.ConnectAsync(endpoint);
        }
        catch
        {
            // Best-effort — ListMembersAsync below will surface whatever's actually wrong.
        }
    }

    /// <summary>
    /// Was a silent best-effort catch until a real user complaint (2026-09-07): "nejde přidat
    /// uživatele, není žádná možnost na výběr" (can't add a user, there's nothing to choose) — an
    /// empty picker with NO explanation looks exactly like a bug whether or not it actually is one.
    /// Surfacing the real reason (not registered yet, relay unreachable, whatever it is) via
    /// <see cref="StatusErrorMessage"/> instead is the honest version of "the app should just work":
    /// when there's something to fix automatically (a dropped connection), <see cref="EnsureConnectedAsync"/>
    /// above fixes it silently; when there genuinely isn't (no registration yet), the user needs to
    /// be told, not left staring at an unexplained empty list.
    /// </summary>
    [RelayCommand]
    private async Task LoadMembersAsync()
    {
        IsLoadingMembers = true;
        StatusErrorMessage = null;
        await EnsureConnectedAsync();
        try
        {
            var members = await _contactDirectoryService.ListMembersAsync();
            Members = new ObservableCollection<SelectableMemberItem>(members.Select(m => new SelectableMemberItem(m.RelayDeviceId, m.DisplayName, m.PublicKey)));
            HasNoMembers = Members.Count == 0;
        }
        catch (Exception ex)
        {
            HasNoMembers = Members.Count == 0;
            StatusErrorMessage = $"Nepodařilo se načíst seznam členů komunity: {ex.Message}";
        }
        finally
        {
            IsLoadingMembers = false;
        }
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        StatusErrorMessage = null;
        CreatedGroupId = null;

        if (string.IsNullOrWhiteSpace(GroupNameText))
        {
            StatusErrorMessage = "Zadejte název skupiny.";
            return;
        }

        var selected = Members.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusErrorMessage = "Vyberte alespoň jednoho člena.";
            return;
        }

        IsBusy = true;
        try
        {
            var configuration = await _transportSettingsRepository.GetAsync();
            var ownDeviceId = configuration?.AssignedDeviceId
                ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
            var ownPublicKey = await _messagingService.GetLocalIdentityPublicKeyAsync();
            await _currentUserService.InitializeAsync();
            var ownDisplayName = _currentUserService.Current.DisplayName;

            var groupId = Guid.NewGuid();
            var group = new GroupChat(groupId, GroupNameText, ownPublicKey);
            await _groupChatRepository.UpsertAsync(group);

            var allMembers = new List<GroupMember> { new(groupId, ownDisplayName, ownPublicKey, ownDeviceId) };
            allMembers.AddRange(selected.Select(m => new GroupMember(groupId, m.DisplayName, m.PublicKey, m.RelayDeviceId)));
            await _groupMemberRepository.ReplaceAllAsync(groupId, allMembers);

            var inviteBlob = ContactCardCodec.Encode(new GroupInviteBlob(
                groupId,
                GroupNameText,
                ownPublicKey,
                allMembers.Select(m => new GroupMemberBlob(m.DisplayName, m.PublicKey, m.RelayDeviceId)).ToList()));

            foreach (var member in selected)
            {
                try
                {
                    // The founder always initiates directly with every member it's adding — no
                    // race to break here (nobody else has this group id yet), unlike the
                    // member-to-member pairing App.xaml.cs's OnGroupInviteReceived has to
                    // deterministically tie-break once the invite fans out to them.
                    if (await _messagingService.FindExistingSessionAsync(member.PublicKey) is null)
                        await _messagingService.CreateSessionAsync(member.DisplayName, member.PublicKey, member.RelayDeviceId);

                    if (_messageTransport.IsConnected)
                        await _messageTransport.SendGroupInviteAsync(member.RelayDeviceId, inviteBlob);
                }
                catch
                {
                    // Best-effort per member — one member's pairing/delivery failing must never
                    // stop the group from being created for everyone else.
                }
            }

            CreatedGroupId = groupId;
            await OpenCreatedGroupCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se vytvořit skupinu: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenCreatedGroupAsync()
    {
        if (CreatedGroupId is not { } groupId) return;
        await Shell.Current.GoToAsync($"{nameof(GroupChatPage)}?groupChatId={groupId}");
    }
}

/// <summary>One selectable row in the "New Group" member picker — a plain <see cref="ObservableObject"/> (not a record, unlike most item types this app uses elsewhere) because <see cref="IsSelected"/> genuinely needs to be mutated in place from a bound CheckBox, not replaced.</summary>
public sealed partial class SelectableMemberItem : ObservableObject
{
    public Guid RelayDeviceId { get; }
    public string DisplayName { get; }
    public byte[] PublicKey { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public SelectableMemberItem(Guid relayDeviceId, string displayName, byte[] publicKey)
    {
        RelayDeviceId = relayDeviceId;
        DisplayName = displayName;
        PublicKey = publicKey;
    }
}
