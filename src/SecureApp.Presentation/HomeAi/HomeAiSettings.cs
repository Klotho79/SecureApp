namespace SecureApp.Presentation.HomeAi;

/// <summary>Per-device settings of the home-AI chat (address, last model, options). The API token is NOT here — it lives in the secure vault.</summary>
internal static class HomeAiSettings
{
    /// <summary>
    /// Not the PC itself (its firewall blocks inbound and LM Studio listens on loopback only): the PC keeps an outbound
    /// reverse SSH tunnel to the Pi (scheduled task "SecureApp HomeAI Tunnel"), and a small container on the Pi
    /// (homeai-forward) offers it at the Pi's address — reachable at home and over the WireGuard tunnel, and the Pi's
    /// address is already allowed for cleartext in network_security_config.xml.
    /// </summary>
    public const string DefaultBaseUrl = "http://192.168.50.8:11234";

    public const string TokenVaultKey = "homeai:api-token";

    public const string DefaultSystemPrompt =
        "Jsi soukromý, věcný poradce. Odpovídej česky, srozumitelně a konkrétně. Když si nejsi jistý, řekni to. " +
        "Nevymýšlej si fakta. U zdravotních, právních a finančních otázek uveď, kdy je dobré obrátit se na odborníka.";

    public static string BaseUrl
    {
        get => Preferences.Default.Get("homeai_base_url", DefaultBaseUrl);
        set => Preferences.Default.Set("homeai_base_url", value);
    }

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
