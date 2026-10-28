using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Readout.Mcp;

/// <summary>
/// Serves <see cref="MeterTools"/> over MCP's Streamable HTTP transport at
/// <c>http://127.0.0.1:{port}/mcp</c>.
/// <para>
/// The socket is bound to the loopback address only, so other computers can't reach it.
/// The HTTP is a small reader and writer (<see cref="LoopbackHttp"/>) rather than
/// HttpListener or ASP.NET Core. ASP.NET Core doesn't exist for Mac Catalyst. HttpListener
/// resolves host names when it starts, and that DNS lookup never returns inside the Mac App
/// Sandbox. On Windows it would also need admin rights for 127.0.0.1.
/// </para>
/// <para>
/// Stateless: every POST gets its own transport and server, so there are no sessions to
/// keep. Served only when addressed to this computer and carrying the bearer token.
/// </para>
/// </summary>
public sealed class McpHttpServer(MeterTools tools, string version, ILoggerFactory? loggerFactory = null) : IAsyncDisposable
{
    public const int DefaultPort = 47121;
    public const string EndpointPath = "/mcp";

    /// <summary>The address the server listens on.</summary>
    public const string Host = "127.0.0.1";

    /// <summary>A client gets this long to send its whole request.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<McpHttpServer>();
    private readonly IReadOnlyList<McpServerTool> _tools = tools.CreateTools();
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _acceptLoop;
    private byte[] _token = [];

    public bool IsRunning => _listener is not null;
    public int Port { get; private set; }
    public Uri Endpoint => new($"http://{Host}:{Port}{EndpointPath}");

    /// <summary>Starts listening. Throws <see cref="SocketException"/> when the port is taken.</summary>
    public void Start(int port, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (IsRunning) throw new InvalidOperationException("The MCP server is already running.");

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        _listener = listener;
        _token = Encoding.UTF8.GetBytes(token);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _stop = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _stop.Token));
        _logger.LogInformation("MCP server listening on {Endpoint}", Endpoint);
    }

    public async Task StopAsync()
    {
        if (_listener is null) return;
        await _stop!.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            if (_acceptLoop is not null) await _acceptLoop.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopping ends the wait for the next connection.
        }
        _stop.Dispose();
        _listener = null;
        _stop = null;
        _acceptLoop = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return; // stopped
            }
            _ = Task.Run(() => HandleConnectionAsync(client, ct), ct);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        var stream = client.GetStream();
        try
        {
            // Only this computer (the socket is loopback-bound; this double-checks).
            if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IPAddress.IsLoopback(remote.Address))
                return;

            HttpRequestData? request;
            using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                readTimeout.CancelAfter(RequestTimeout);
                try
                {
                    request = await LoopbackHttp.ReadRequestAsync(stream, readTimeout.Token).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    await LoopbackHttp.WriteResponseAsync(stream, HttpStatusCode.BadRequest, null, null, ct).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return; // too slow
                }
            }
            if (request is null) return;

            var (status, headers, body) = await RespondAsync(request, ct).ConfigureAwait(false);
            await LoopbackHttp.WriteResponseAsync(stream, status, headers, body, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The client went away.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopping.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP request failed");
            try
            {
                await LoopbackHttp.WriteResponseAsync(stream, HttpStatusCode.InternalServerError, null, null, ct).ConfigureAwait(false);
            }
            catch (Exception writeEx) when (writeEx is IOException or SocketException or ObjectDisposedException) { }
        }
    }

    private async Task<(HttpStatusCode, List<KeyValuePair<string, string>>?, byte[]?)> RespondAsync(HttpRequestData request, CancellationToken ct)
    {
        if (Check(request) is { } refusal)
            return (refusal, refusal == HttpStatusCode.Unauthorized ? [new("WWW-Authenticate", "Bearer")] : null, null);

        if (request.Method != "POST")
        {
            // Stateless: no server-to-client stream (GET) and no sessions to end (DELETE).
            return (HttpStatusCode.MethodNotAllowed, [new("Allow", "POST")], null);
        }

        JsonRpcMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(request.Body, McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonRpcMessage>());
        }
        catch (JsonException)
        {
            message = null;
        }
        if (message is null)
            return (HttpStatusCode.BadRequest, null, null);

        // The SDK writes its answer as server-sent events; it's short, so it's sent whole.
        using var output = new MemoryStream();
        var transport = new StreamableHttpServerTransport(_loggerFactory) { Stateless = true };
        var server = McpServer.Create(transport, CreateOptions(), _loggerFactory);
        var run = server.RunAsync(ct);
        bool wrote;
        try
        {
            wrote = await transport.HandlePostRequestAsync(message, output, ct).ConfigureAwait(false);
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            try { await run.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            await server.DisposeAsync().ConfigureAwait(false);
        }

        if (!wrote)
            return (HttpStatusCode.Accepted, null, null); // a notification or a response: nothing to answer
        return (HttpStatusCode.OK, [new("Content-Type", "text/event-stream"), new("Cache-Control", "no-cache, no-store")], output.ToArray());
    }

    /// <summary>Why a request is refused, or null to serve it.</summary>
    private HttpStatusCode? Check(HttpRequestData request)
    {
        if (!string.Equals(request.Path.TrimEnd('/'), EndpointPath, StringComparison.Ordinal))
            return HttpStatusCode.NotFound;

        // A web page can't reach it by pointing its own name at 127.0.0.1 (DNS rebinding):
        // the request must be addressed to this computer, and a browser's Origin must be local.
        if (!IsLocalHost(HostName(request.Header("Host"))))
            return HttpStatusCode.Forbidden;
        if (request.Header("Origin") is { Length: > 0 } origin
            && (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || !IsLocalHost(originUri.Host)))
            return HttpStatusCode.Forbidden;

        var auth = request.Header("Authorization");
        const string prefix = "Bearer ";
        if (auth is null || !auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth[prefix.Length..].Trim()), _token))
            return HttpStatusCode.Unauthorized;

        return null;
    }

    /// <summary>The host part of a Host header ("127.0.0.1:47121" → "127.0.0.1", "[::1]:80" → "[::1]").</summary>
    private static string? HostName(string? hostHeader)
    {
        if (string.IsNullOrEmpty(hostHeader)) return null;
        if (hostHeader.StartsWith('['))
        {
            var close = hostHeader.IndexOf(']', StringComparison.Ordinal);
            return close > 0 ? hostHeader[..(close + 1)] : hostHeader;
        }
        var colon = hostHeader.LastIndexOf(':');
        return colon >= 0 ? hostHeader[..colon] : hostHeader;
    }

    private static bool IsLocalHost(string? host) =>
        host is not null && (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1" || host == "[::1]" || host == "::1");

    private McpServerOptions CreateOptions()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var tool in _tools)
            collection.Add(tool);
        return new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "readout", Title = "Readout (EEVblog 121GW)", Version = version },
            ServerInstructions = MeterTools.Instructions,
            ToolCollection = collection,
        };
    }
}

/// <summary>An MCP client the app can write setup text for.</summary>
public enum McpClientKind
{
    ClaudeCode,
    ClaudeDesktop,
    VsCode,
    Cursor,
}

/// <summary>What a client needs to use the server: the address, the header, or a ready-made setup.</summary>
public static class McpSetup
{
    private const string ServerName = "readout";
    // Readable for pasting: no \u escapes for characters like + or &.
    private static readonly System.Text.Json.JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Address(int port) => $"http://{McpHttpServer.Host}:{port}{McpHttpServer.EndpointPath}";

    public static string AuthorizationHeader(string token) => $"Authorization: Bearer {token}";

    /// <summary>The text to paste into <paramref name="client"/>'s setup.</summary>
    public static string For(McpClientKind client, int port, string token) => client switch
    {
        McpClientKind.ClaudeCode => ClaudeCodeCommand(port, token),
        McpClientKind.ClaudeDesktop => ClaudeDesktopConfig(port, token),
        McpClientKind.VsCode => VsCodeConfig(port, token),
        McpClientKind.Cursor => CursorConfig(port, token),
        _ => throw new ArgumentOutOfRangeException(nameof(client)),
    };

    /// <summary>The command that adds the server to Claude Code.</summary>
    public static string ClaudeCodeCommand(int port, string token) =>
        $"claude mcp add --transport http {ServerName} {Address(port)} --header \"{AuthorizationHeader(token)}\"";

    /// <summary>
    /// For claude_desktop_config.json. Claude Desktop starts local servers as programs, so it
    /// connects through mcp-remote; the token goes in an environment variable because on
    /// Windows a space inside an argument splits it.
    /// </summary>
    public static string ClaudeDesktopConfig(int port, string token) => Json(new System.Text.Json.Nodes.JsonObject
    {
        ["mcpServers"] = new System.Text.Json.Nodes.JsonObject
        {
            [ServerName] = new System.Text.Json.Nodes.JsonObject
            {
                ["command"] = "npx",
                ["args"] = new System.Text.Json.Nodes.JsonArray(
                    "mcp-remote@latest", Address(port), "--allow-http", "--header", "Authorization:${READOUT_AUTH}"),
                ["env"] = new System.Text.Json.Nodes.JsonObject { ["READOUT_AUTH"] = $"Bearer {token}" },
            },
        },
    });

    /// <summary>For VS Code's mcp.json (MCP: Open User Configuration).</summary>
    public static string VsCodeConfig(int port, string token) => Json(new System.Text.Json.Nodes.JsonObject
    {
        ["servers"] = new System.Text.Json.Nodes.JsonObject
        {
            [ServerName] = new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "http",
                ["url"] = Address(port),
                ["headers"] = new System.Text.Json.Nodes.JsonObject { ["Authorization"] = $"Bearer {token}" },
            },
        },
    });

    /// <summary>For Cursor's ~/.cursor/mcp.json.</summary>
    public static string CursorConfig(int port, string token) => Json(new System.Text.Json.Nodes.JsonObject
    {
        ["mcpServers"] = new System.Text.Json.Nodes.JsonObject
        {
            [ServerName] = new System.Text.Json.Nodes.JsonObject
            {
                ["url"] = Address(port),
                ["headers"] = new System.Text.Json.Nodes.JsonObject { ["Authorization"] = $"Bearer {token}" },
            },
        },
    });

    private static string Json(System.Text.Json.Nodes.JsonObject value) => value.ToJsonString(Indented);
}
