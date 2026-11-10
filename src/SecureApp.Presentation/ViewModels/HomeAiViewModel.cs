using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.HomeAi;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.ViewModels;

public enum HomeAiMessageKind { User, Assistant, Step, Error }

/// <summary>One bubble in the chat. Text is observable because the answer streams in piece by piece.</summary>
public sealed partial class HomeAiMessage : ObservableObject
{
    public HomeAiMessage(HomeAiMessageKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public HomeAiMessageKind Kind { get; }

    public bool IsUser => Kind == HomeAiMessageKind.User;

    public bool IsAssistant => Kind == HomeAiMessageKind.Assistant;

    public bool IsStep => Kind == HomeAiMessageKind.Step;

    public bool IsError => Kind == HomeAiMessageKind.Error;

    [ObservableProperty]
    public partial string Text { get; set; }
}

/// <summary>
/// Private chat with the user's own LM Studio at home (2026-11-10, user's ask: only on their phone, "bez rizika leaku
/// soukromých informací"). The conversation and any attached file live only in memory — nothing is stored, logged or
/// sent anywhere but to the home server over the home Wi-Fi or the WireGuard tunnel. Admin + the user's own Galaxy
/// S23+ only (see <see cref="IsAccessGranted"/>); the entry card in Settings has the same gate.
/// </summary>
public sealed partial class HomeAiViewModel : ObservableObject
{
    private static readonly int[] AutoOffMinutesChoices = [0, 5, 10, 15, 30, 60];

    private readonly HomeAiClient _client;
    private readonly ProblemDecomposer _decomposer;
    private readonly DocumentReader _documentReader;
    private readonly ISecureVaultKeyStore _vault;
    private readonly ICurrentUserService _currentUserService;
    private readonly RolePreviewService _rolePreviewService;
    private CancellationTokenSource? _generationCts;
    private string? _attachedText;
    private readonly Stopwatch _scrollThrottle = Stopwatch.StartNew();

    public HomeAiViewModel(HomeAiClient client, ISecureVaultKeyStore vault, ICurrentUserService currentUserService, RolePreviewService rolePreviewService)
    {
        _client = client;
        _decomposer = new ProblemDecomposer(client);
        _documentReader = new DocumentReader(client);
        _vault = vault;
        _currentUserService = currentUserService;
        _rolePreviewService = rolePreviewService;

        BaseUrlText = HomeAiSettings.BaseUrl;
        UseDecompose = HomeAiSettings.Decompose;
        SelectedAutoOffIndex = Math.Max(0, Array.IndexOf(AutoOffMinutesChoices, HomeAiSettings.AutoOffMinutes));
        StatusText = "Nepřipojeno.";
    }

    public ObservableCollection<HomeAiModel> Models { get; } = [];

    public ObservableCollection<HomeAiMessage> Messages { get; } = [];

    public IReadOnlyList<string> AutoOffChoices { get; } = ["Nikdy (drží v paměti)", "po 5 min nečinnosti", "po 10 min nečinnosti", "po 15 min nečinnosti", "po 30 min nečinnosti", "po 60 min nečinnosti"];

    /// <summary>Raised when the list should scroll to its last bubble (throttled while an answer streams).</summary>
    public event Action? ScrollRequested;

    [ObservableProperty]
    public partial string BaseUrlText { get; set; }

    [ObservableProperty]
    public partial string TokenText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial HomeAiModel? SelectedModel { get; set; }

    [ObservableProperty]
    public partial string PromptText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool UseDecompose { get; set; }

    [ObservableProperty]
    public partial int SelectedAutoOffIndex { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsGenerating { get; set; }

    [ObservableProperty]
    public partial string? AttachmentName { get; set; }

    [ObservableProperty]
    public partial string? AttachmentInfo { get; set; }

    public bool HasAttachment => !string.IsNullOrEmpty(AttachmentName);

    /// <summary>Admin on the user's own Galaxy S23+ only: this is the private assistant of one person, not a feature for the community (see <see cref="HomeAiAccess"/>).</summary>
    public bool IsAccessGranted =>
        _rolePreviewService.Resolve(_currentUserService.Current.Role) == Role.Admin
        && HomeAiAccess.IsAllowedDevice;

    public bool IsAccessDenied => !IsAccessGranted;

    public bool CanSend => !IsGenerating && !IsBusy && SelectedModel is not null && !string.IsNullOrWhiteSpace(PromptText);

    public bool CanLoad => !IsGenerating && !IsBusy && SelectedModel is { IsLoaded: false };

    /// <summary>"Vypnout AI": anything at all is in memory.</summary>
    public bool CanUnload => !IsGenerating && !IsBusy && Models.Any(m => m.IsLoaded);

    public bool CanStop => IsGenerating;

    public bool CanConnect => !IsGenerating && !IsBusy;

    public bool CanAttach => !IsGenerating && !IsBusy;

    partial void OnIsBusyChanged(bool value) => NotifyButtons();

    partial void OnIsGeneratingChanged(bool value) => NotifyButtons();

    partial void OnPromptTextChanged(string value) => NotifyButtons();

    partial void OnSelectedModelChanged(HomeAiModel? value)
    {
        if (value is not null)
            HomeAiSettings.ModelKey = value.Key;
        NotifyButtons();
    }

    partial void OnUseDecomposeChanged(bool value) => HomeAiSettings.Decompose = value;

    partial void OnSelectedAutoOffIndexChanged(int value)
    {
        if (value >= 0 && value < AutoOffMinutesChoices.Length)
            HomeAiSettings.AutoOffMinutes = AutoOffMinutesChoices[value];
    }

    partial void OnAttachmentNameChanged(string? value) => OnPropertyChanged(nameof(HasAttachment));

    private void NotifyButtons()
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(CanUnload));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanAttach));
    }

    /// <summary>Called when the page opens: fills in the saved token and tries the saved address.</summary>
    public async Task InitializeAsync()
    {
        OnPropertyChanged(nameof(IsAccessGranted));
        OnPropertyChanged(nameof(IsAccessDenied));
        if (!IsAccessGranted)
            return;

        if (string.IsNullOrEmpty(TokenText) && await _vault.RetrieveSecretAsync(HomeAiSettings.TokenVaultKey) is { Length: > 0 } token)
            TokenText = Encoding.UTF8.GetString(token);

        if (Models.Count == 0)
            await RefreshModelsAsync();
    }

    /// <summary>The server address + token, plus the idle auto-off the chat requests ask the server for.</summary>
    private HomeAiEndpoint Endpoint()
    {
        var minutes = AutoOffMinutesChoices[Math.Clamp(SelectedAutoOffIndex, 0, AutoOffMinutesChoices.Length - 1)];
        return new(BaseUrlText.Trim(), string.IsNullOrWhiteSpace(TokenText) ? null : TokenText.Trim(), minutes > 0 ? minutes * 60 : null);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        HomeAiSettings.BaseUrl = BaseUrlText.Trim();
        await _vault.StoreSecretAsync(HomeAiSettings.TokenVaultKey, Encoding.UTF8.GetBytes(TokenText.Trim()));
        await RefreshModelsAsync();
    }

    private async Task RefreshModelsAsync(string? keepKey = null)
    {
        IsBusy = true;
        try
        {
            var models = await _client.ListModelsAsync(Endpoint());
            var wanted = keepKey ?? SelectedModel?.Key ?? HomeAiSettings.ModelKey;

            Models.Clear();
            foreach (var model in models)
                Models.Add(model);

            SelectedModel = Models.FirstOrDefault(m => m.Key == wanted) ?? Models.FirstOrDefault(m => m.IsLoaded) ?? Models.FirstOrDefault();

            var loaded = Models.Where(m => m.IsLoaded).Select(m => m.DisplayName).ToList();
            StatusText = Models.Count == 0
                ? "Server odpovídá, ale nemá žádný model."
                : loaded.Count > 0 ? $"Připojeno · v paměti: {string.Join(", ", loaded)}" : "Připojeno · AI je vypnutá (žádný model není v paměti).";
        }
        catch (Exception ex) when (ex is HomeAiException or HttpRequestException)
        {
            Models.Clear();
            SelectedModel = null;
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyButtons();
        }
    }

    /// <summary>Loads the chosen model and keeps it in memory until "Vypnout AI" (a model the chat loads by itself is unloaded after the idle time instead).</summary>
    [RelayCommand]
    private async Task LoadModelAsync()
    {
        if (SelectedModel is not { } model)
            return;

        IsBusy = true;
        StatusText = $"Načítám {model.DisplayName}… (velký model může trvat i minutu)";
        try
        {
            await _client.LoadModelAsync(Endpoint(), model.Key);
        }
        catch (Exception ex) when (ex is HomeAiException or HttpRequestException)
        {
            StatusText = ex.Message;
            IsBusy = false;
            return;
        }

        IsBusy = false;
        await RefreshModelsAsync(model.Key);
    }

    /// <summary>"Vypnout AI": every loaded model is unloaded, so nothing stays in the PC's memory.</summary>
    [RelayCommand]
    private async Task UnloadAllAsync()
    {
        var loaded = Models.Where(m => m.IsLoaded).ToList();
        if (loaded.Count == 0)
            return;

        _generationCts?.Cancel();
        IsBusy = true;
        StatusText = "Vypínám AI…";
        try
        {
            foreach (var instanceId in loaded.SelectMany(m => m.LoadedInstanceIds))
                await _client.UnloadModelAsync(Endpoint(), instanceId);
        }
        catch (Exception ex) when (ex is HomeAiException or HttpRequestException)
        {
            StatusText = ex.Message;
            IsBusy = false;
            return;
        }

        IsBusy = false;
        await RefreshModelsAsync(SelectedModel?.Key);
    }

    [RelayCommand]
    private void NewChat()
    {
        _generationCts?.Cancel();
        Messages.Clear();
        RemoveAttachment();
    }

    [RelayCommand]
    private void Stop() => _generationCts?.Cancel();

    /// <summary>Picks a file whose text the AI should work with (text files, PDF with a text layer, Word, Excel); it stays in memory only.</summary>
    [RelayCommand]
    private async Task AttachFileAsync()
    {
        FileResult? picked;
        try
        {
            picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Vyberte soubor pro AI" });
        }
        catch (Exception ex)
        {
            StatusText = $"Nepodařilo se otevřít výběr souborů: {ex.Message}";
            return;
        }

        if (picked is null)
            return;

        IsBusy = true;
        StatusText = $"Čtu {picked.FileName}…";
        try
        {
            var (text, truncated) = await DocumentTextExtractor.ExtractAsync(picked);
            _attachedText = text;
            AttachmentName = picked.FileName;
            AttachmentInfo = $"{text.Length:N0} znaků" + (truncated ? " (zkráceno)" : string.Empty);
            StatusText = $"Příloha načtená: {picked.FileName}. Napište, co s ní má AI udělat.";
        }
        catch (Exception ex)
        {
            StatusText = $"Soubor se nepodařilo přečíst: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RemoveAttachment()
    {
        _attachedText = null;
        AttachmentName = null;
        AttachmentInfo = null;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = PromptText.Trim();
        if (text.Length == 0 || SelectedModel is not { } selected || IsGenerating)
            return;

        var attachedText = _attachedText;
        var attachedName = AttachmentName ?? "příloha";

        PromptText = string.Empty;
        Messages.Add(new HomeAiMessage(HomeAiMessageKind.User, attachedText is null ? text : $"{text}\n📎 {attachedName}"));
        ScrollRequested?.Invoke();

        IsGenerating = true;
        _generationCts = new CancellationTokenSource();
        var ct = _generationCts.Token;
        HomeAiMessage? answer = null;
        try
        {
            var endpoint = Endpoint();
            var systemPrompt = string.IsNullOrWhiteSpace(HomeAiSettings.SystemPrompt) ? HomeAiSettings.DefaultSystemPrompt : HomeAiSettings.SystemPrompt;

            // No explicit load: the first request loads the model on demand and carries the idle TTL, so with the
            // auto-off set the server unloads it again by itself (a model loaded by hand with "Načíst" stays instead).
            StatusText = selected.IsLoaded
                ? (UseDecompose ? "Rozkládám problém…" : "Odpovídám…")
                : $"Načítám {selected.DisplayName} a odpovídám… (poprvé to chvíli trvá)";

            if (attachedText is not null && !DocumentReader.FitsInOnePrompt(attachedText, selected.UsableContext))
            {
                // Longer than the model's context: read it in pieces and answer from the notes.
                answer = await ConsumeAsync(_documentReader.AnswerAsync(endpoint, selected.Key, systemPrompt, text, attachedName, attachedText, selected.UsableContext, ct));
            }
            else
            {
                var content = attachedText is null ? text : $"Dokument „{attachedName}“:\n<<<\n{attachedText}\n>>>\n\n{text}";
                if (UseDecompose)
                    answer = await ConsumeAsync(_decomposer.SolveAsync(endpoint, selected.Key, systemPrompt, content, ct));
                else
                    answer = await RunPlainAsync(endpoint, selected.Key, systemPrompt, content, ct);
            }

            StatusText = "Hotovo.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Zastaveno.";
        }
        catch (Exception ex) when (ex is HomeAiException or HttpRequestException)
        {
            if (answer is { Text.Length: 0 })
                Messages.Remove(answer);
            Messages.Add(new HomeAiMessage(HomeAiMessageKind.Error, ex.Message));
            StatusText = "Chyba.";
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
            ScrollRequested?.Invoke();
        }

        await RefreshModelsAsync(selected.Key); // shows what is in memory now
    }

    /// <summary>A normal chat: the last messages of the conversation plus this turn (with the attached document, if any) go to the model.</summary>
    private async Task<HomeAiMessage> RunPlainAsync(HomeAiEndpoint endpoint, string model, string systemPrompt, string currentUserContent, CancellationToken ct)
    {
        var history = new List<HomeAiChatMessage> { new("system", systemPrompt) };
        history.AddRange(Messages
            .Where(m => m.IsUser || m.IsAssistant)
            .SkipLast(1) // the user bubble just added: this turn is sent below with the document
            .TakeLast(12)
            .Select(m => new HomeAiChatMessage(m.IsUser ? "user" : "assistant", m.Text)));
        history.Add(new("user", currentUserContent));

        var answer = new HomeAiMessage(HomeAiMessageKind.Assistant, string.Empty);
        Messages.Add(answer);
        await foreach (var piece in _client.StreamChatAsync(endpoint, model, history, 0.5, null, ct))
        {
            answer.Text += piece;
            ThrottledScroll();
        }

        return answer;
    }

    /// <summary>Steps (problem decomposition) stay visible as grey notes; the final answer streams into its own bubble.</summary>
    private async Task<HomeAiMessage?> ConsumeAsync(IAsyncEnumerable<DecomposeEvent> events)
    {
        HomeAiMessage? finalAnswer = null;
        await foreach (var ev in events)
        {
            switch (ev)
            {
                case PlanReady plan:
                    Messages.Add(new HomeAiMessage(HomeAiMessageKind.Step,
                        "Plán:\n" + string.Join("\n", plan.Questions.Select((q, i) => $"{i + 1}. {q}"))));
                    break;
                case StepStarted started:
                    StatusText = $"Krok {started.Index}/{started.Total}…";
                    break;
                case StepFinished done:
                    Messages.Add(new HomeAiMessage(HomeAiMessageKind.Step, $"Krok {done.Index}/{done.Total} — {done.Question}\n{done.Answer}"));
                    break;
                case ChunkStarted chunk:
                    StatusText = $"Čtu dokument: úsek {chunk.Index}/{chunk.Total}…";
                    break;
                case DocumentShortened shortened:
                    Messages.Add(new HomeAiMessage(HomeAiMessageKind.Step,
                        $"Dokument je velmi dlouhý, přečetl jsem jen jeho začátek ({shortened.UsedChars:N0} z {shortened.TotalChars:N0} znaků)."));
                    break;
                case FinalToken token:
                    if (finalAnswer is null)
                    {
                        StatusText = "Skládám odpověď…";
                        finalAnswer = new HomeAiMessage(HomeAiMessageKind.Assistant, string.Empty);
                        Messages.Add(finalAnswer);
                    }

                    finalAnswer.Text += token.Text;
                    break;
            }

            ThrottledScroll();
        }

        return finalAnswer;
    }

    private void ThrottledScroll()
    {
        if (_scrollThrottle.ElapsedMilliseconds < 200)
            return;
        _scrollThrottle.Restart();
        ScrollRequested?.Invoke();
    }
}
