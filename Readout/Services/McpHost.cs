#if MACCATALYST || WINDOWS
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Readout.Core.Connection;
using Readout.Core.Logging;
using Readout.Mcp;

namespace Readout.Services;

/// <summary>
/// Runs the MCP server for AI assistants while it's turned on in Settings, and restarts it
/// when the port or token changes. Desktop only.
/// </summary>
public sealed class McpHost : IDisposable
{
    private readonly AppSettings _settings;
    private readonly McpHttpServer _server;
    private readonly ILogger<McpHost> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (bool Enabled, int Port, string Token) _applied;

    public McpHost(AppSettings settings, MeterSession session, SessionStore store, RecordingAutosave autosave, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _logger = loggerFactory.CreateLogger<McpHost>();
        var tools = new MeterTools(session, store, autosave, new MainThreadUiThread());
        _server = new McpHttpServer(tools, AppInfo.Current.VersionString, loggerFactory);
        settings.Changed += (_, _) => _ = ApplyAsync();
    }

    /// <summary>"Off", "Running" or why it couldn't start.</summary>
    public string Status { get; private set; } = "Off";
    public bool IsRunning => _server.IsRunning;
    public event EventHandler? StatusChanged;

    /// <summary>Starts, stops or restarts the server to match the settings.</summary>
    public async Task ApplyAsync()
    {
        try
        {
            await ApplyCoreAsync();
        }
        catch (Exception ex)
        {
            // Called fire-and-forget from settings changes: never let a failure go unseen.
            _logger.LogError(ex, "applying MCP settings failed");
            SetStatus($"Couldn't start: {ex.Message}");
        }
    }

    private async Task ApplyCoreAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var wanted = (_settings.McpEnabled, _settings.McpPort, wanted: _settings.McpEnabled ? _settings.McpToken : "");
            if (wanted == _applied && wanted.McpEnabled == _server.IsRunning) return;

            await _server.StopAsync();
            _applied = wanted;
            if (!wanted.McpEnabled)
            {
                SetStatus("Off");
                return;
            }

            try
            {
                // Off the main thread: starting a listener can block, and the window mustn't.
                var token = _settings.McpToken;
                await Task.Run(() => _server.Start(wanted.McpPort, token));
                SetStatus("Running");
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "MCP server couldn't start on port {Port}", wanted.McpPort);
                SetStatus($"Couldn't start on port {wanted.McpPort}: {ex.Message}. Try another port.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP server couldn't start");
                SetStatus($"Couldn't start: {ex.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        // At shutdown: give the server a moment to stop, then let go.
        _server.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _gate.Dispose();
    }

    private void SetStatus(string status)
    {
        Status = status;
        MainThread.BeginInvokeOnMainThread(() => StatusChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>The meter session belongs to the main thread; MCP requests hop over to it.</summary>
    private sealed class MainThreadUiThread : IUiThread
    {
        public Task<T> InvokeAsync<T>(Func<T> work) => MainThread.InvokeOnMainThreadAsync(work);
        public Task<T> InvokeAsync<T>(Func<Task<T>> work) => MainThread.InvokeOnMainThreadAsync(work);
    }
}
#endif
