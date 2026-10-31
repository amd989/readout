using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Readout.Core.Connection;
using Readout.Core.Logging;
using Readout.Core.Protocol;
using Readout.Core.Simulation;
using Readout.Mcp;

namespace Readout.Tests;

/// <summary>The MCP server end to end: the SDK's own client, over HTTP, against a fake meter.</summary>
public sealed class McpServerTests : IAsyncLifetime
{
    private const string Token = "test-token";
    private static readonly DiscoveredMeter Meter = new("aa:bb", "121GW", -62);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly FakeBluetooth _bluetooth = new();
    private readonly SimulatedMeterConnection _demo;
    private readonly MeterSession _session;
    private readonly SessionStore _store = new(Path.Combine(Path.GetTempPath(), "readout-mcp-" + Guid.NewGuid().ToString("N")));
    private readonly McpHttpServer _server;
    private McpClient? _client;

    public McpServerTests()
    {
        _demo = new SimulatedMeterConnection(_time);
        _session = new MeterSession(_bluetooth, _demo, _time);
        var tools = new MeterTools(_session, _store, autosave: null, new InlineUiThread(), _time) { SettleTime = TimeSpan.Zero };
        _server = new McpHttpServer(tools, "1.0-test");
    }

    public async ValueTask InitializeAsync()
    {
        _server.Start(FreePort(), Token);
        _client = await Connect(Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        await _server.DisposeAsync();
        _demo.Dispose();
        if (Directory.Exists(_store.Directory)) Directory.Delete(_store.Directory, recursive: true);
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private Task<McpClient> Connect(string token) => McpClient.CreateAsync(
        new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = _server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        }),
        cancellationToken: TestContext.Current.CancellationToken);

    private async Task<(JsonElement Json, bool IsError, string Text)> Call(string tool, Dictionary<string, object?>? args = null)
    {
        var result = await _client!.CallToolAsync(tool, args ?? [], cancellationToken: TestContext.Current.CancellationToken);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        var json = result.IsError == true ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (json, result.IsError == true, text);
    }

    private async Task ConnectMeterWithReading(int raw = 12345)
    {
        await _session.ConnectAsync(Meter, TestContext.Current.CancellationToken);
        _bluetooth.Send(new PacketBuilder { MainRangeIndex = 1, MainRawValue = raw }.BuildPacket());
    }

    [Fact]
    public async Task ListsTheTools()
    {
        var tools = await _client!.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["clear_log", "get_reading", "get_recent_readings", "get_session", "get_status", "list_sessions", "press_button", "save_session", "set_logging", "sync_clock"],
            tools.Select(t => t.Name).Order());
        Assert.Equal("readout", _client!.ServerInfo?.Name);
    }

    [Fact]
    public async Task GetReading_ReturnsWhatTheMeterShows()
    {
        await ConnectMeterWithReading();

        var (json, isError, _) = await Call("get_reading");

        Assert.False(isError);
        Assert.Equal("DC voltage", json.GetProperty("mode").GetString());
        Assert.Equal("12.345 V", json.GetProperty("display").GetString());
        Assert.Equal(12.345, json.GetProperty("value").GetDouble(), 6);
        Assert.Equal("V", json.GetProperty("unit").GetString());
    }

    [Fact]
    public async Task GetReading_WithoutAMeter_SaysSo()
    {
        var (_, isError, text) = await Call("get_reading");

        Assert.True(isError);
        Assert.Contains("No meter is connected", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatus_ReportsConnectionAndSignal()
    {
        await ConnectMeterWithReading();
        _bluetooth.SignalStrength = -62;

        var (json, _, _) = await Call("get_status");

        Assert.Equal("Connected", json.GetProperty("connection").GetString());
        Assert.Equal("121GW", json.GetProperty("meter").GetString());
        Assert.Equal(-62, json.GetProperty("signal_dbm").GetInt32());
        Assert.Equal("12.345 V", json.GetProperty("reading").GetProperty("display").GetString());
    }

    [Fact]
    public async Task PressButton_SendsTheKey()
    {
        await ConnectMeterWithReading();

        var (json, isError, _) = await Call("press_button", new() { ["button"] = "hold" });
        await Call("press_button", new() { ["button"] = "RANGE", ["longPress"] = true });

        Assert.False(isError);
        Assert.Equal("HOLD", json.GetProperty("button").GetString());
        Assert.Equal([(MeterKey.Hold, false), (MeterKey.Range, true)], _bluetooth.Keys);
    }

    [Fact]
    public async Task PressButton_RefusesToHoldPeak_AndUnknownButtons()
    {
        await ConnectMeterWithReading();

        var (_, holdPeakError, holdPeakText) = await Call("press_button", new() { ["button"] = "PEAK", ["longPress"] = true });
        var (_, unknownError, _) = await Call("press_button", new() { ["button"] = "POWER" });

        Assert.True(holdPeakError);
        Assert.Contains("Bluetooth off", holdPeakText, StringComparison.Ordinal);
        Assert.True(unknownError);
        Assert.Empty(_bluetooth.Keys);
    }

    [Fact]
    public async Task RecentReadings_CoverTheWindow_WithStatistics()
    {
        await _session.ConnectAsync(Meter, TestContext.Current.CancellationToken);
        foreach (var raw in new[] { 10000, 12000, 11000 })
        {
            _bluetooth.Send(new PacketBuilder { MainRangeIndex = 1, MainRawValue = raw }.BuildPacket());
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        var (json, _, _) = await Call("get_recent_readings", new() { ["seconds"] = 60 });

        Assert.Equal(3, json.GetProperty("count").GetInt32());
        var stats = json.GetProperty("statistics");
        Assert.Equal(10.0, stats.GetProperty("min").GetDouble(), 6);
        Assert.Equal(12.0, stats.GetProperty("max").GetDouble(), 6);
        Assert.Equal(2.0, stats.GetProperty("peak_to_peak").GetDouble(), 6);
    }

    [Fact]
    public async Task SaveSession_ThenListAndRead_AndTheLogStartsOver()
    {
        await ConnectMeterWithReading();
        _bluetooth.Send(new PacketBuilder { MainRangeIndex = 1, MainRawValue = 12000 }.BuildPacket());

        var (saved, _, _) = await Call("save_session", new() { ["name"] = "Bench supply" });
        var (list, _, _) = await Call("list_sessions");
        var id = saved.GetProperty("id").GetString()!;
        var (detail, _, _) = await Call("get_session", new() { ["id"] = id });

        Assert.Equal(0, _session.Log.Count);
        Assert.Equal("Bench supply", list.GetProperty("sessions")[0].GetProperty("name").GetString());
        Assert.Equal(2, detail.GetProperty("readings").GetArrayLength());
    }

    [Fact]
    public async Task SetLogging_PausesAndTellsTheApp()
    {
        var changes = 0;
        _session.LoggingChanged += (_, _) => changes++;

        var (json, _, _) = await Call("set_logging", new() { ["enabled"] = false });

        Assert.False(json.GetProperty("logging").GetBoolean());
        Assert.False(_session.IsLogging);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task WrongOrMissingToken_IsRefused()
    {
        using var http = new HttpClient();
        var body = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json");

        using var none = await http.PostAsync(_server.Endpoint, body, TestContext.Current.CancellationToken);
        using var wrong = new HttpRequestMessage(HttpMethod.Post, _server.Endpoint) { Content = body };
        wrong.Headers.Authorization = new("Bearer", "nope");
        using var wrongResponse = await http.SendAsync(wrong, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(() => Connect("nope"));
    }

    [Fact]
    public async Task ARequestNotAddressedToLocalhost_IsRefused()
    {
        // As a web page using DNS rebinding would send it: right port, someone else's host name.
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, _server.Endpoint)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = "evil.example";
        request.Headers.Authorization = new("Bearer", Token);

        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"got {response.StatusCode}");
    }

    private async Task<string> RawRequest(string request)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _server.Port, TestContext.Current.CancellationToken);
        var stream = tcp.GetStream();
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(request), TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AChunkedRequestBody_IsRead()
    {
        const string json = """{"jsonrpc":"2.0","id":7,"method":"tools/list"}""";
        var request =
            $"POST /mcp HTTP/1.1\r\nHost: 127.0.0.1:{_server.Port}\r\nAuthorization: Bearer {Token}\r\n" +
            "Accept: application/json, text/event-stream\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n" +
            $"{10:x}\r\n{json[..10]}\r\n{json.Length - 10:x}\r\n{json[10..]}\r\n0\r\n\r\n";

        var response = await RawRequest(request);

        Assert.StartsWith("HTTP/1.1 200", response, StringComparison.Ordinal);
        Assert.Contains("get_reading", response, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET /mcp HTTP/1.1", "405")]
    [InlineData("POST /other HTTP/1.1", "404")]
    [InlineData("NONSENSE", "400")]
    public async Task OtherRequests_GetTheRightStatus(string requestLine, string status)
    {
        var response = await RawRequest(
            $"{requestLine}\r\nHost: 127.0.0.1:{_server.Port}\r\nAuthorization: Bearer {Token}\r\nContent-Length: 0\r\n\r\n");

        Assert.StartsWith($"HTTP/1.1 {status}", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APortInUse_FailsToStart()
    {
        await using var second = new McpHttpServer(
            new MeterTools(_session, _store, null, new InlineUiThread(), _time), "1.0-test");

        Assert.ThrowsAny<SocketException>(() => second.Start(_server.Port, Token));
    }

    [Fact]
    public void ThinKeepsTheEndsAndSpacesTheRest()
    {
        var thinned = MeterTools.Thin(Enumerable.Range(0, 1000).ToList(), 5);
        Assert.Equal([0, 249, 499, 749, 999], thinned);
    }

    [Fact]
    public void SetupText_ForEachClient_HasTheAddressAndToken()
    {
        var address = $"http://{McpHttpServer.Host}:47121/mcp";

        var vsCode = JsonDocument.Parse(McpSetup.For(McpClientKind.VsCode, 47121, "abc")).RootElement.GetProperty("servers").GetProperty("readout");
        Assert.Equal("http", vsCode.GetProperty("type").GetString());
        Assert.Equal(address, vsCode.GetProperty("url").GetString());
        Assert.Equal("Bearer abc", vsCode.GetProperty("headers").GetProperty("Authorization").GetString());

        var cursor = JsonDocument.Parse(McpSetup.For(McpClientKind.Cursor, 47121, "abc")).RootElement.GetProperty("mcpServers").GetProperty("readout");
        Assert.Equal(address, cursor.GetProperty("url").GetString());
        Assert.Equal("Bearer abc", cursor.GetProperty("headers").GetProperty("Authorization").GetString());

        var desktop = JsonDocument.Parse(McpSetup.For(McpClientKind.ClaudeDesktop, 47121, "abc")).RootElement.GetProperty("mcpServers").GetProperty("readout");
        Assert.Equal("npx", desktop.GetProperty("command").GetString());
        Assert.Equal(["mcp-remote@latest", address, "--allow-http", "--header", "Authorization:${READOUT_AUTH}"],
            desktop.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("Bearer abc", desktop.GetProperty("env").GetProperty("READOUT_AUTH").GetString());

        Assert.Equal(address, McpSetup.Address(47121));
        Assert.Equal("Authorization: Bearer abc", McpSetup.AuthorizationHeader("abc"));
    }

    [Fact]
    public void ClaudeCodeCommand_HasTheEndpointAndToken() =>
        Assert.Equal(
            $"claude mcp add --transport http readout http://{McpHttpServer.Host}:47121/mcp --header \"Authorization: Bearer abc\"",
            McpSetup.ClaudeCodeCommand(47121, "abc"));
}
