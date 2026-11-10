using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// soukromých informací"). The conversation lives only in memory — nothing is stored, logged or sent anywhere but to
/// the home server over the home Wi-Fi or the WireGuard tunnel. Admin + Android only (see
/// <see cref="IsAccessGranted"/>); the entry card in Settings has the same gate.
/// </summary>
public sealed partial class HomeAiViewModel : ObservableObject
{
    private readonly HomeAiClient _client;
    private readonly ProblemDecomposer _decomposer;
    private readonly ISecureVaultKeyStore _vault;
    private readonly ICurrentUserService _currentUserService;
    private readonly RolePreviewService _rolePreviewService;
    private CancellationTokenSource? _generationCts;
    private readonly Stopwatch _scrollThrottle = Stopwatch.StartNew();

    public HomeAiViewModel(HomeAiClient client, ISecureVaultKeyStore vault, ICurrentUserService currentUserService, RolePreviewService rolePreviewService)
    {
        _client = client;
        _decomposer = new ProblemDecomposer(client);
        _vault = vault;
        _currentUserService = currentUserService;
        _rolePreviewService = rolePreviewService;

        BaseUrlText = HomeAiSettings.BaseUrl;
        UseDecompose = HomeAiSettings.Decompose;
        StatusText = "Nepřipojeno.";
    }

    public ObservableCollection<HomeAiModel> Models { get; } = [];

    public ObservableCollection<HomeAiMessage> Messages { get; } = [];

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
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsGenerating { get; set; }

    /// <summary>Admin on the user's own Galaxy S23+ only: this is the private assistant of one person, not a feature for the community (see <see cref="HomeAiAccess"/>).</summary>
    public bool IsAccessGranted =>
        _rolePreviewService.Resolve(_currentUserService.Current.Role) == Role.Admin
        && HomeAiAccess.IsAllowedDevice;

    public bool IsAccessDenied => !IsAccessGranted;

    public bool CanSend => !IsGenerating && !IsBusy && SelectedModel is not null && !string.IsNullOrWhiteSpace(PromptText);

    public bool CanLoad => !IsGenerating && !IsBusy && SelectedModel is { IsLoaded: false };

    public bool CanUnload => !IsGenerating && !IsBusy && SelectedModel is { IsLoaded: true };

    public bool CanStop => IsGenerating;

    public bool CanConnect => !IsGenerating && !IsBusy;

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

    private void NotifyButtons()
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(CanUnload));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanConnect));
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

    private HomeAiEndpoint Endpoint() => new(BaseUrlText.Trim(), string.IsNullOrWhiteSpace(TokenText) ? null : TokenText.Trim());

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
                : loaded.Count > 0 ? $"Připojeno · v paměti: {string.Join(", ", loaded)}" : "Připojeno · žádný model není v paměti.";
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
        }
    }

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

    [RelayCommand]
    private async Task UnloadModelAsync()
    {
        if (SelectedModel is not { IsLoaded: true } model)
            return;

        IsBusy = true;
        StatusText = $"Uvolňuji {model.DisplayName}…";
        try
        {
            foreach (var instanceId in model.LoadedInstanceIds)
                await _client.UnloadModelAsync(Endpoint(), instanceId);
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

    [RelayCommand]
    private void NewChat()
    {
        _generationCts?.Cancel();
        Messages.Clear();
    }

    [RelayCommand]
    private void Stop() => _generationCts?.Cancel();

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = PromptText.Trim();
        if (text.Length == 0 || SelectedModel is not { } selected || IsGenerating)
            return;

        PromptText = string.Empty;
        Messages.Add(new HomeAiMessage(HomeAiMessageKind.User, text));
        ScrollRequested?.Invoke();

        IsGenerating = true;
        _generationCts = new CancellationTokenSource();
        var ct = _generationCts.Token;
        HomeAiMessage? answer = null;
        try
        {
            var endpoint = Endpoint();
            if (!selected.IsLoaded)
            {
                StatusText = $"Načítám {selected.DisplayName}…";
                await _client.LoadModelAsync(endpoint, selected.Key, ct);
                await RefreshModelsAsync(selected.Key);
            }

            StatusText = UseDecompose ? "Rozkládám problém…" : "Odpovídám…";
            var systemPrompt = string.IsNullOrWhiteSpace(HomeAiSettings.SystemPrompt) ? HomeAiSettings.DefaultSystemPrompt : HomeAiSettings.SystemPrompt;

            if (UseDecompose)
                answer = await RunDecomposedAsync(endpoint, selected.Key, systemPrompt, text, ct);
            else
                answer = await RunPlainAsync(endpoint, selected.Key, systemPrompt, ct);

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
    }

    /// <summary>A normal chat: the last messages of the conversation go to the model together with the system prompt.</summary>
    private async Task<HomeAiMessage> RunPlainAsync(HomeAiEndpoint endpoint, string model, string systemPrompt, CancellationToken ct)
    {
        var history = new List<HomeAiChatMessage> { new("system", systemPrompt) };
        history.AddRange(Messages
            .Where(m => m.IsUser || m.IsAssistant)
            .TakeLast(12)
            .Select(m => new HomeAiChatMessage(m.IsUser ? "user" : "assistant", m.Text)));

        var answer = new HomeAiMessage(HomeAiMessageKind.Assistant, string.Empty);
        Messages.Add(answer);
        await StreamIntoAsync(answer, _client.StreamChatAsync(endpoint, model, history, 0.5, null, ct));
        return answer;
    }

    /// <summary>The problem is split into short steps, each answered on its own, then assembled (see <see cref="ProblemDecomposer"/>); the steps stay visible as grey notes.</summary>
    private async Task<HomeAiMessage?> RunDecomposedAsync(HomeAiEndpoint endpoint, string model, string systemPrompt, string problem, CancellationToken ct)
    {
        HomeAiMessage? finalAnswer = null;
        await foreach (var ev in _decomposer.SolveAsync(endpoint, model, systemPrompt, problem, ct))
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

    private async Task StreamIntoAsync(HomeAiMessage target, IAsyncEnumerable<string> pieces)
    {
        await foreach (var piece in pieces)
        {
            target.Text += piece;
            ThrottledScroll();
        }
    }

    private void ThrottledScroll()
    {
        if (_scrollThrottle.ElapsedMilliseconds < 200)
            return;
        _scrollThrottle.Restart();
        ScrollRequested?.Invoke();
    }
}
