using System.Globalization;
using System.Net;
using System.Text;

namespace Readout.Mcp;

/// <summary>One HTTP request as the MCP server needs it: method, path, headers and body.</summary>
internal sealed record HttpRequestData(string Method, string Path, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// The little HTTP/1.1 the MCP server needs: read one request (Content-Length or chunked
/// body), write one response, close. Not a general web server: there's no keep-alive,
/// and sizes are capped.
/// </summary>
internal static class LoopbackHttp
{
    public const int MaxHeaderBytes = 32 * 1024;
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    /// <summary>Reads a request, or returns null when the connection closes first. Throws <see cref="InvalidDataException"/> on a malformed one.</summary>
    public static async Task<HttpRequestData?> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        var reader = new BufferedReader(stream);

        var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (requestLine is null) return null;
        var parts = requestLine.Split(' ');
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            throw new InvalidDataException("Bad request line.");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Headers ended early.");
            if (line.Length == 0) break;
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) throw new InvalidDataException("Bad header line.");
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var existing) ? $"{existing}, {value}" : value;
        }
        reader.HeadersDone();

        byte[] body;
        if (headers.TryGetValue("Transfer-Encoding", out var encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            body = await reader.ReadChunkedAsync(ct).ConfigureAwait(false);
        }
        else if (headers.TryGetValue("Content-Length", out var lengthText))
        {
            if (!int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length > MaxBodyBytes)
                throw new InvalidDataException("Bad or too large Content-Length.");
            body = await reader.ReadExactlyAsync(length, ct).ConfigureAwait(false);
        }
        else
        {
            body = [];
        }

        var path = parts[1];
        var query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0) path = path[..query];
        return new HttpRequestData(parts[0].ToUpperInvariant(), path, headers, body);
    }

    /// <summary>Writes a whole response with a Content-Length, and asks the client to close.</summary>
    public static async Task WriteResponseAsync(
        Stream stream, HttpStatusCode status, IEnumerable<KeyValuePair<string, string>>? headers, byte[]? body, CancellationToken ct)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {(int)status} {Reason(status)}\r\n");
        foreach (var (name, value) in headers ?? [])
            text.Append(CultureInfo.InvariantCulture, $"{name}: {value}\r\n");
        text.Append(CultureInfo.InvariantCulture, $"Content-Length: {body?.Length ?? 0}\r\n");
        text.Append("Connection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(text.ToString()), ct).ConfigureAwait(false);
        if (body is { Length: > 0 })
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string Reason(HttpStatusCode status) => status switch
    {
        HttpStatusCode.OK => "OK",
        HttpStatusCode.Accepted => "Accepted",
        HttpStatusCode.BadRequest => "Bad Request",
        HttpStatusCode.Unauthorized => "Unauthorized",
        HttpStatusCode.Forbidden => "Forbidden",
        HttpStatusCode.NotFound => "Not Found",
        HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
        HttpStatusCode.RequestEntityTooLarge => "Payload Too Large",
        _ => "Internal Server Error",
    };

    /// <summary>Reads lines and exact byte counts from a stream, sharing one buffer.</summary>
    private sealed class BufferedReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[8192];
        private int _start;
        private int _end;
        private int _headerBytes;
        private bool _inBody;

        /// <summary>From here on lines are chunk framing, which the header size cap doesn't cover.</summary>
        public void HeadersDone() => _inBody = true;

        private async Task<bool> FillAsync(CancellationToken ct)
        {
            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            if (_end == _buffer.Length) throw new InvalidDataException("Line too long.");
            var read = await stream.ReadAsync(_buffer.AsMemory(_end), ct).ConfigureAwait(false);
            _end += read;
            return read > 0;
        }

        /// <summary>A CRLF-terminated line without the CRLF, or null at the end of the stream before any byte.</summary>
        public async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            while (true)
            {
                for (var i = _start; i + 1 < _end; i++)
                {
                    if (_buffer[i] != '\r' || _buffer[i + 1] != '\n') continue;
                    var line = Encoding.ASCII.GetString(_buffer, _start, i - _start);
                    if (!_inBody)
                    {
                        _headerBytes += i + 2 - _start;
                        if (_headerBytes > MaxHeaderBytes) throw new InvalidDataException("Headers too large.");
                    }
                    _start = i + 2;
                    return line;
                }
                if (!await FillAsync(ct).ConfigureAwait(false))
                    return _start == _end ? null : throw new InvalidDataException("Line ended early.");
            }
        }

        public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
        {
            var result = new byte[count];
            var copied = 0;
            while (copied < count)
            {
                if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                    throw new InvalidDataException("Body ended early.");
                var n = Math.Min(count - copied, _end - _start);
                Buffer.BlockCopy(_buffer, _start, result, copied, n);
                _start += n;
                copied += n;
            }
            return result;
        }

        public async Task<byte[]> ReadChunkedAsync(CancellationToken ct)
        {
            using var body = new MemoryStream();
            while (true)
            {
                var sizeLine = await ReadLineAsync(ct).ConfigureAwait(false) ?? throw new InvalidDataException("Chunk ended early.");
                var semicolon = sizeLine.IndexOf(';', StringComparison.Ordinal);
                var sizeText = semicolon >= 0 ? sizeLine[..semicolon] : sizeLine;
                if (!int.TryParse(sizeText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) || size < 0)
                    throw new InvalidDataException("Bad chunk size.");
                if (size == 0)
                {
                    // Trailers, if any, end with an empty line.
                    while (!string.IsNullOrEmpty(await ReadLineAsync(ct).ConfigureAwait(false))) { }
                    return body.ToArray();
                }
                if (body.Length + size > MaxBodyBytes) throw new InvalidDataException("Body too large.");
                body.Write(await ReadExactlyAsync(size, ct).ConfigureAwait(false));
                if (await ReadLineAsync(ct).ConfigureAwait(false) != "")
                    throw new InvalidDataException("Bad chunk end.");
            }
        }
    }
}
