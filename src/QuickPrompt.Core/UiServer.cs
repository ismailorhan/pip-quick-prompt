namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal HTTP/1.1 server on 127.0.0.1 for the response window.
    /// Every URL contains an unguessable per-session token; the Host header is checked
    /// (DNS rebinding) and state-changing requests need a custom header (cross-site POSTs).
    /// </summary>
    public sealed class UiServer : IDisposable
    {
        private const Int32 MaxHeaderBytes = 32 * 1024;
        private const Int32 MaxBodyBytes = 4 * 1024 * 1024;

        private readonly ChatManager _manager;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private TcpListener _listener;

        public UiServer(ChatManager manager)
        {
            this._manager = manager;
        }

        public Int32 Port { get; private set; }

        public void Start()
        {
            if (this._listener != null)
            {
                return;
            }

            this._listener = new TcpListener(IPAddress.Loopback, 0);
            this._listener.Start();
            this.Port = ((IPEndPoint)this._listener.LocalEndpoint).Port;
            _ = Task.Run(this.AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (!this._cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await this._listener.AcceptTcpClientAsync(this._cts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    if (this._cts.IsCancellationRequested)
                    {
                        return;
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                    continue;
                }

                _ = Task.Run(() => this.HandleClientAsync(client));
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                try
                {
                    var request = await ReadRequestAsync(stream, this._cts.Token).ConfigureAwait(false);
                    if (request == null)
                    {
                        return;
                    }

                    var response = await this.RouteAsync(request).ConfigureAwait(false);
                    await WriteResponseAsync(stream, response).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this._manager.Host.Log("UI server failed to handle a request", ex);
                    try
                    {
                        await WriteResponseAsync(stream, HttpResponse.Text(500, "Internal error")).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Connection gone.
                    }
                }
            }
        }

        private async Task<HttpResponse> RouteAsync(HttpRequest request)
        {
            if (!this.IsAllowedHost(request.Header("host")))
            {
                return HttpResponse.Text(403, "Forbidden");
            }

            if (request.Path == "/favicon.ico")
            {
                return new HttpResponse { Status = 204 };
            }

            // /s/{token}/{action}
            var parts = request.Path.Trim('/').Split('/');
            if (parts.Length < 2 || parts[0] != "s" || !this._manager.TryGetSession(parts[1], out var session))
            {
                return HttpResponse.Text(404, "This window's session has ended. Close it and try again from the Actions Ring.");
            }

            var action = parts.Length > 2 ? parts[2] : "";

            if (request.Method == "GET" && action == "")
            {
                return HttpResponse.Html(ChatPage.Html);
            }

            if (request.Method == "GET" && action == "state")
            {
                Int64.TryParse(request.Query("v"), out var since);
                await session.WaitForChangeAsync(since, TimeSpan.FromSeconds(20), this._cts.Token).ConfigureAwait(false);
                return HttpResponse.Json(session.ToJson(request.Query("tab") != "1"));
            }

            if (request.Method == "GET" && action == "history")
            {
                return HttpResponse.Json(this._manager.GetHistoryJson());
            }

            if (request.Method == "GET" && action == "settings")
            {
                return HttpResponse.Json(this._manager.GetSettingsJson());
            }

            if (request.Method == "POST" && action == "closed")
            {
                // navigator.sendBeacon cannot set custom headers; cancelling is harmless.
                session.Cancel();
                return new HttpResponse { Status = 204 };
            }

            if (request.Method != "POST" || request.Header("x-quickprompt") != "1")
            {
                return HttpResponse.Text(400, "Bad request");
            }

            var body = ParseJson(request.Body);
            switch (action)
            {
                case "resume":
                    var resumed = this._manager.ResumeConversation(session, GetString(body, "id"));
                    return resumed ? HttpResponse.Json("{\"ok\":true}") : HttpResponse.Json("{\"ok\":false}", 409);

                case "forget":
                    this._manager.DeleteConversation(GetString(body, "id"));
                    return HttpResponse.Json(this._manager.GetHistoryJson());

                case "clearhistory":
                    this._manager.ClearHistory();
                    return HttpResponse.Json(this._manager.GetHistoryJson());

                case "settings":
                    this._manager.SaveSettings(body);
                    return HttpResponse.Json(this._manager.GetSettingsJson());

                case "send":
                    var started = session.StartTurnFromPopup(GetString(body, "instruction"), GetString(body, "context"));
                    return started ? HttpResponse.Json("{\"ok\":true}") : HttpResponse.Json("{\"ok\":false}", 409);

                case "cancel":
                    session.Cancel();
                    return HttpResponse.Json("{\"ok\":true}");

                case "copy":
                    this._manager.Host.SetClipboardText(GetString(body, "text"));
                    return HttpResponse.Json("{\"ok\":true}");

                case "paste":
                    // The page closes its window right after this call; paste once focus is back in the previous app.
                    var text = GetString(body, "text");
                    _ = Task.Run(() => this._manager.PasteTextAsync(text, null, delayBeforePasteMs: 450));
                    return HttpResponse.Json("{\"ok\":true}");
            }

            return HttpResponse.Text(404, "Not found");
        }

        private Boolean IsAllowedHost(String host) =>
            host == $"127.0.0.1:{this.Port}" || host == $"localhost:{this.Port}";

        private static Dictionary<String, String> ParseJson(Byte[] body)
        {
            var result = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            if (body == null || body.Length == 0)
            {
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            result[prop.Name] = prop.Value.GetString();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Empty result.
            }

            return result;
        }

        private static String GetString(Dictionary<String, String> body, String key) =>
            body.TryGetValue(key, out var value) ? value ?? "" : "";

        private static async Task<HttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new MemoryStream();
            var chunk = new Byte[8192];
            var headerEnd = -1;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(chunk, 0, chunk.Length, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
                headerEnd = IndexOfHeaderEnd(buffer.GetBuffer(), (Int32)buffer.Length);
                if (headerEnd < 0 && buffer.Length > MaxHeaderBytes)
                {
                    return null;
                }
            }

            var all = buffer.GetBuffer();
            var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
            var lines = headerText.Split("\r\n");
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
            {
                return null;
            }

            var request = new HttpRequest { Method = requestLine[0].ToUpperInvariant() };
            var target = requestLine[1];
            var q = target.IndexOf('?');
            request.Path = Uri.UnescapeDataString(q >= 0 ? target.Substring(0, q) : target);
            if (q >= 0)
            {
                foreach (var pair in target.Substring(q + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = pair.IndexOf('=');
                    var key = Uri.UnescapeDataString(eq >= 0 ? pair.Substring(0, eq) : pair);
                    var value = eq >= 0 ? Uri.UnescapeDataString(pair.Substring(eq + 1)) : "";
                    request.QueryValues[key] = value;
                }
            }

            for (var i = 1; i < lines.Length; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    request.Headers[lines[i].Substring(0, colon).Trim().ToLowerInvariant()] = lines[i].Substring(colon + 1).Trim();
                }
            }

            Int32.TryParse(request.Header("content-length"), out var contentLength);
            if (contentLength < 0 || contentLength > MaxBodyBytes)
            {
                return null;
            }

            var bodyStart = headerEnd + 4;
            var body = new Byte[contentLength];
            var have = Math.Min(contentLength, (Int32)buffer.Length - bodyStart);
            Array.Copy(all, bodyStart, body, 0, have);
            while (have < contentLength)
            {
                var read = await stream.ReadAsync(body, have, contentLength - have, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }

                have += read;
            }

            request.Body = body;
            return request;
        }

        private static Int32 IndexOfHeaderEnd(Byte[] data, Int32 length)
        {
            for (var i = 3; i < length; i++)
            {
                if (data[i - 3] == '\r' && data[i - 2] == '\n' && data[i - 1] == '\r' && data[i] == '\n')
                {
                    return i - 3;
                }
            }

            return -1;
        }

        private static async Task WriteResponseAsync(NetworkStream stream, HttpResponse response)
        {
            var body = response.Body ?? Array.Empty<Byte>();
            var reason = response.Status switch
            {
                200 => "OK",
                204 => "No Content",
                400 => "Bad Request",
                403 => "Forbidden",
                404 => "Not Found",
                409 => "Conflict",
                _ => "Error",
            };

            var header = new StringBuilder()
                .Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(reason).Append("\r\n")
                .Append("Content-Type: ").Append(response.ContentType).Append("\r\n")
                .Append("Content-Length: ").Append(body.Length).Append("\r\n")
                .Append("Cache-Control: no-store\r\n")
                .Append("X-Content-Type-Options: nosniff\r\n")
                .Append("Referrer-Policy: no-referrer\r\n")
                .Append("Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src data:; base-uri 'none'; form-action 'none'\r\n")
                .Append("Connection: close\r\n\r\n")
                .ToString();

            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            if (body.Length > 0)
            {
                await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            }

            await stream.FlushAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            this._cts.Cancel();
            try
            {
                this._listener?.Stop();
            }
            catch (Exception)
            {
                // Ignore.
            }
        }

        private sealed class HttpRequest
        {
            public String Method { get; set; } = "GET";

            public String Path { get; set; } = "/";

            public Dictionary<String, String> Headers { get; } = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<String, String> QueryValues { get; } = new Dictionary<String, String>(StringComparer.Ordinal);

            public Byte[] Body { get; set; }

            public String Header(String name) => this.Headers.TryGetValue(name, out var v) ? v : "";

            public String Query(String name) => this.QueryValues.TryGetValue(name, out var v) ? v : "";
        }

        private sealed class HttpResponse
        {
            public Int32 Status { get; set; } = 200;

            public String ContentType { get; set; } = "text/plain; charset=utf-8";

            public Byte[] Body { get; set; }

            public static HttpResponse Text(Int32 status, String text) =>
                new HttpResponse { Status = status, Body = Encoding.UTF8.GetBytes(text) };

            public static HttpResponse Html(String html) =>
                new HttpResponse { ContentType = "text/html; charset=utf-8", Body = Encoding.UTF8.GetBytes(html) };

            public static HttpResponse Json(String json, Int32 status = 200) =>
                new HttpResponse { Status = status, ContentType = "application/json; charset=utf-8", Body = Encoding.UTF8.GetBytes(json) };
        }
    }
}
