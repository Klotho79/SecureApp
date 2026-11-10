namespace SecureApp.Presentation.HomeAi;

/// <summary>
/// Per-device settings of the home-AI chat (last model, options). The LM Studio on the home PC is reached through the
/// relay's /homeai/ proxy (see relay HomeAiProxy.cs): the PC keeps an outbound reverse SSH tunnel to the Pi (scheduled
/// task "SecureApp HomeAI Tunnel"), a container on the Pi (homeai-forward) offers it on the LAN, and the relay forwards
/// to that — so the phone needs no direct route to the PC and works on mobile data as well as at home.
/// </summary>
internal static class HomeAiSettings
{
    public const string DefaultSystemPrompt =
        "Jsi soukromý, věcný poradce. Odpovídej česky, srozumitelně a konkrétně. Když si nejsi jistý, řekni to. " +
        "Nevymýšlej si fakta. U zdravotních, právních a finančních otázek uveď, kdy je dobré obrátit se na odborníka.";

    public static string ModelKey
    {
        get => Preferences.Default.Get("homeai_model", string.Empty);
        set => Preferences.Default.Set("homeai_model", value);
    }

    /// <summary>Minutes of idleness after which a model loaded by the chat is unloaded by the server (0 = never). Default 10.</summary>
    public static int AutoOffMinutes
    {
        get => Preferences.Default.Get("homeai_autooff_min", 10);
        set => Preferences.Default.Set("homeai_autooff_min", value);
    }

    public static bool Decompose
    {
        get => Preferences.Default.Get("homeai_decompose", false);
        set => Preferences.Default.Set("homeai_decompose", value);
    }

    public static string SystemPrompt
    {
        get => Preferences.Default.Get("homeai_system_prompt", DefaultSystemPrompt);
        set => Preferences.Default.Set("homeai_system_prompt", value);
    }
}
