# Using Readout with Claude (MCP)

On Mac and Windows, Readout can run a small [Model Context Protocol](https://modelcontextprotocol.io)
server so an AI assistant such as Claude can use your 121GW: read it, look at recent
readings, press its buttons and save sessions. It's off until you turn it on.

## Turn it on

1. Connect your meter in Readout (or try the demo meter).
2. **Settings → AI assistants → Let AI assistants use the meter.** The status line shows
   `Listening on http://127.0.0.1:47121/mcp`.
3. Tap **Copy Claude Code command** and paste it in a terminal. It looks like:

   ```bash
   claude mcp add --transport http readout http://127.0.0.1:47121/mcp --header "Authorization: Bearer <token>"
   ```

4. Start a new Claude Code session and ask, for example, "What's the meter reading?" or
   "Log the supply voltage for a minute and tell me how much it ripples."

Other MCP clients work too: point them at `http://127.0.0.1:47121/mcp` (Streamable HTTP)
with the header `Authorization: Bearer <token>`.

## Tools

| Tool | What it does |
|---|---|
| `get_status` | Connection, meter, signal, battery, logging, latest reading |
| `get_reading` | What the meter shows now: value and unit (also in SI base units), the small display, mode, lit LCD symbols |
| `get_recent_readings` | Logged readings over the last `seconds`, thinned to `maxPoints`, with min/max/average/peak-to-peak |
| `press_button` | Presses RANGE, HOLD, REL, PEAK, MODE, MINMAX, MEM or SETUP, short or `longPress`, and returns the reading after |
| `set_logging` | Pauses or resumes logging |
| `clear_log` | Clears the log and starts a new recording |
| `save_session` | Saves the log as a named session (it appears in Sessions) and starts a new recording |
| `list_sessions` | Saved sessions, newest first |
| `get_session` | One saved session's details and readings |

The assistant can't turn the meter's dial; the dial picks what's measured, and MODE cycles
within it. Holding 1ms PEAK is refused, because it turns the meter's Bluetooth off and would
end the connection.

## Security

- The server listens on 127.0.0.1 only, so other computers can't connect to it.
- Every request needs the token. **Settings → New token** replaces it; clients set up with the
  old one stop working.
- Requests must be addressed to this computer (`127.0.0.1` or `localhost`), and a browser's
  `Origin` must be local, so a web page can't reach the server through DNS rebinding.
- Nothing is sent anywhere by the app: the assistant asks, the app answers.

## How it's built

`Readout.Mcp` holds the tools (`MeterTools`) and the server (`McpHttpServer`). It uses the
official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.Core`)
for the protocol, in stateless mode: each POST gets its own transport and server.

The HTTP itself is a small reader and writer on a loopback-bound `TcpListener`, for three reasons:

- ASP.NET Core (the SDK's `ModelContextProtocol.AspNetCore`) isn't available for Mac Catalyst.
- `HttpListener` does a DNS lookup when it starts, which never returns inside the Mac App Sandbox.
- On Windows, `HttpListener` needs admin rights to listen on 127.0.0.1.

The Mac app needs the `com.apple.security.network.server` entitlement for it.

`Readout.Mcp` is plain `net10.0` with no UI, so a future Linux front end or a command-line tool
can host the same tools.
