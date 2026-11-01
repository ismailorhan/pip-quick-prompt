namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>ChatGPT models through the OpenAI Chat Completions API, streamed (server-sent events).</summary>
    public sealed class OpenAiApiProvider : IAiProvider
    {
        public const String SecretName = "openai-api-key";

        private static readonly HttpClient SharedHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        private readonly Func<QuickPromptSettings> _settings;
        private readonly Func<String> _apiKey;
        private readonly String _baseUrl;

        /// <param name="baseUrl">Only for tests (a local fake server).</param>
        public OpenAiApiProvider(Func<QuickPromptSettings> settings, Func<String> apiKey, String baseUrl = null)
        {
            this._settings = settings;
            this._apiKey = apiKey;
            this._baseUrl = (baseUrl ?? "https://api.openai.com").TrimEnd('/');
        }

        public String Id => ProviderIds.Codex;

        public String DisplayName => "ChatGPT";

        public async IAsyncEnumerable<AiEvent> RunAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var key = this._apiKey();
            if (String.IsNullOrWhiteSpace(key))
            {
                yield return new AiEvent(AiEventKind.Error, "No OpenAI API key yet. Open Settings (⚙) and add one, or switch ChatGPT to CLI.");
                yield break;
            }

            var model = String.IsNullOrWhiteSpace(request.Model) ? this._settings().OpenAiApiModel : request.Model.Trim();
            var messages = new List<Object>();
            if (!String.IsNullOrWhiteSpace(request.SystemPrompt))
            {
                messages.Add(new { role = "system", content = request.SystemPrompt });
            }

            messages.Add(new { role = "user", content = request.Prompt ?? "" });
            var body = JsonSerializer.Serialize(new { model, stream = true, messages });

            var http = new HttpRequestMessage(HttpMethod.Post, this._baseUrl + "/v1/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            http.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            yield return new AiEvent(AiEventKind.Status, "Waiting for the answer…");

            var (response, sendError) = await TrySendAsync(http, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                yield return new AiEvent(AiEventKind.Error, cancellationToken.IsCancellationRequested ? "Cancelled." : "Could not reach the OpenAI API: " + sendError);
                yield break;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                    yield return new AiEvent(AiEventKind.Error, DescribeError((Int32)response.StatusCode, errorBody, model));
                    yield break;
                }

                var parser = new OpenAiStreamParser();
                using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (true)
                {
                    String line;
                    try
                    {
                        line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        line = null;
                    }

                    if (line == null)
                    {
                        break;
                    }

                    foreach (var ev in parser.Feed(line))
                    {
                        yield return ev;
                    }

                    if (parser.IsFinished)
                    {
                        break;
                    }
                }

                foreach (var ev in parser.Complete(cancellationToken.IsCancellationRequested))
                {
                    yield return ev;
                }
            }
        }

        private static async Task<(HttpResponseMessage, String)> TrySendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                var response = await SharedHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                return (response, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        public static String DescribeError(Int32 status, String body, String model)
        {
            var message = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    message = Json.GetString(error, "message");
                }
            }
            catch (JsonException)
            {
                // Not JSON.
            }

            return status switch
            {
                401 => "The OpenAI API key was rejected. Check it in Settings (⚙).",
                404 => $"Model \"{model}\" was not found. Pick another model in Settings (⚙) or in the preset.",
                429 => "OpenAI API rate limit or quota reached." + (message.Length > 0 ? " " + message : ""),
                _ => $"OpenAI API error ({status})" + (message.Length > 0 ? ": " + message : "."),
            };
        }
    }

    /// <summary>Parses Chat Completions server-sent events ("data: {...}" lines, "data: [DONE]").</summary>
    public sealed class OpenAiStreamParser
    {
        private readonly StringBuilder _text = new StringBuilder();
        private String _finishReason = "";
        private Boolean _failed;

        public Boolean IsFinished { get; private set; }

        public IEnumerable<AiEvent> Feed(String line)
        {
            if (this.IsFinished || line == null || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                return Array.Empty<AiEvent>();
            }

            var data = line.Substring(5).Trim();
            if (data == "[DONE]")
            {
                this.IsFinished = true;
                return Array.Empty<AiEvent>();
            }

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var error))
                {
                    this.IsFinished = true;
                    this._failed = true;
                    return new[] { new AiEvent(AiEventKind.Error, "OpenAI API error: " + Json.GetString(error, "message")) };
                }

                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                {
                    return Array.Empty<AiEvent>();
                }

                var choice = choices[0];
                var finish = Json.GetString(choice, "finish_reason");
                if (finish.Length > 0)
                {
                    this._finishReason = finish;
                }

                if (choice.TryGetProperty("delta", out var delta))
                {
                    var piece = Json.GetString(delta, "content");
                    if (piece.Length > 0)
                    {
                        this._text.Append(piece);
                        return new[] { new AiEvent(AiEventKind.Delta, piece) };
                    }

                    var refusal = Json.GetString(delta, "refusal");
                    if (refusal.Length > 0)
                    {
                        this._text.Append(refusal);
                        return new[] { new AiEvent(AiEventKind.Delta, refusal) };
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore malformed lines.
            }

            return Array.Empty<AiEvent>();
        }

        public IEnumerable<AiEvent> Complete(Boolean cancelled)
        {
            if (this._failed)
            {
                yield break;
            }

            if (cancelled)
            {
                yield return new AiEvent(AiEventKind.Error, "Cancelled.");
                yield break;
            }

            if (this._text.Length == 0)
            {
                yield return new AiEvent(AiEventKind.Error, "The OpenAI API returned no answer" + (this._finishReason.Length > 0 ? $" ({this._finishReason})." : "."));
                yield break;
            }

            yield return new AiEvent(AiEventKind.Final, this._text.ToString());
        }
    }
}
