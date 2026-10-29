namespace Readout.Services;

/// <summary>The user's settings, kept in Preferences. <see cref="Changed"/> fires after any change.</summary>
public sealed class AppSettings
{
    private readonly IPreferences _prefs;

    public AppSettings(IPreferences prefs) => _prefs = prefs;

    public event EventHandler? Changed;

    public bool AutoReconnect
    {
        get => _prefs.Get("auto-reconnect", true);
        set => Set("auto-reconnect", value);
    }

    public bool ContinuitySound
    {
        get => _prefs.Get("continuity-sound", true);
        set => Set("continuity-sound", value);
    }

    /// <summary>PCs have nothing to vibrate, so Windows hides the setting and never vibrates.</summary>
    public static bool CanVibrate => DeviceInfo.Platform != DevicePlatform.WinUI;

    public bool ContinuityVibration
    {
        get => CanVibrate && _prefs.Get("continuity-vibration", true);
        set => Set("continuity-vibration", value);
    }

    /// <summary>Beep below this resistance (Ω).</summary>
    public double ContinuityOnBelow
    {
        get => _prefs.Get("continuity-on-below", 30.0);
        set => Set("continuity-on-below", Math.Max(0, value));
    }

    /// <summary>Stop above this resistance (Ω).</summary>
    public double ContinuityOffAbove
    {
        get => _prefs.Get("continuity-off-above", 50.0);
        set => Set("continuity-off-above", Math.Max(0, value));
    }

    /// <summary>Serve the meter to AI assistants over MCP (desktop only). Off unless the user turns it on.</summary>
    public bool McpEnabled
    {
        get => _prefs.Get("mcp-enabled", false);
        set => Set("mcp-enabled", value);
    }

    public const int DefaultMcpPort = 47121;

    public int McpPort
    {
        get => _prefs.Get("mcp-port", DefaultMcpPort);
        set => Set("mcp-port", Math.Clamp(value, 1024, 65535));
    }

    /// <summary>The bearer token MCP clients must send. Made on first use; see <see cref="NewMcpToken"/>.</summary>
    public string McpToken
    {
        get
        {
            var token = _prefs.Get("mcp-token", "");
            if (token.Length == 0)
            {
                token = MakeToken();
                _prefs.Set("mcp-token", token);
            }
            return token;
        }
    }

    /// <summary>Replaces the token, so clients set up with the old one stop working.</summary>
    public void NewMcpToken() => Set("mcp-token", MakeToken());

    private static string MakeToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void Set<T>(string key, T value)
    {
        _prefs.Set(key, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
