namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    using Anthropic;
    using Anthropic.Exceptions;
    using Anthropic.Models.Beta.Messages;

    /// <summary>
    /// Claude through the Anthropic API (official C# SDK), streamed. Much faster than the CLI because
    /// no process starts per request. Uses server-side refusal fallbacks.
    /// </summary>
    public sealed class AnthropicApiProvider : IAiProvider
    {
        public const String SecretName = "anthropic-api-key";

        private readonly Func<QuickPromptSettings> _settings;
        private readonly Func<String> _apiKey;
        private readonly String _baseUrl;

        /// <param name="baseUrl">Only for tests (a local fake server).</param>
        public AnthropicApiProvider(Func<QuickPromptSettings> settings, Func<String> apiKey, String baseUrl = null)
        {
            this._settings = settings;
            this._apiKey = apiKey;
            this._baseUrl = baseUrl;
        }

        public String Id => ProviderIds.Claude;

        public String DisplayName => "Claude";

        /// <summary>"haiku" / "sonnet" / "opus" (as used with the CLI) map to the current API model ids.</summary>
        public static String ResolveModel(String requested, String fallback)
        {
            var model = String.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim();
            return model.ToLowerInvariant() switch
            {
                "haiku" => "claude-haiku-5-5",
                "sonnet" => "claude-sonnet-5-5",
                "opus" => "claude-opus-5-5",
                "fable" => "claude-fable-5-1",
                _ => model,
            };
        }

        /// <summary>
        /// The conversation as separate messages. Assistant turns carry only their text (thinking is never
        /// stored), so there are no thinking blocks to replay and the history is append-only.
        /// </summary>
        public static List<BetaMessageParam> BuildMessages(AiRequest request)
        {
            var messages = new List<BetaMessageParam>();
            if (String.IsNullOrEmpty(request.Message))
            {
                messages.Add(new BetaUserMessageParam { Content = UserContent(request.Prompt ?? "", request.AllImages()) });
                return messages;
            }

            var history = request.History ?? Array.Empty<KeyValuePair<String, String>>();
            for (var i = 0; i < history.Count; i++)
            {
                var turn = history[i];
                var images = request.HistoryImages != null && i < request.HistoryImages.Count ? request.HistoryImages[i] : null;
                messages.Add(turn.Key == "assistant"
                    ? new BetaAssistantMessageParam { Content = turn.Value ?? "" }
                    : new BetaUserMessageParam { Content = UserContent(turn.Value ?? "", images) });
            }

            messages.Add(new BetaUserMessageParam { Content = UserContent(request.Message, request.Images) });
            return messages;
        }

        /// <summary>Text alone, or the pictures followed by the text.</summary>
        private static BetaMessageContentParam UserContent(String text, IReadOnlyList<ImageAttachment> images)
        {
            if (images == null || images.Count == 0)
            {
                return text;
            }

            var blocks = new List<BetaContentBlockParam>();
            foreach (var image in images)
            {
                if (image.IsPdf)
                {
                    blocks.Add(new BetaRequestDocumentBlock
                    {
                        Source = new BetaBase64PdfSource { Data = image.Data },
                        Title = String.IsNullOrEmpty(image.Name) ? null : image.Name,
                    });
                    continue;
                }

                blocks.Add(new BetaImageBlockParam
                {
                    Source = new BetaBase64ImageSource { Data = image.Data, MediaType = MediaTypeOf(image.MediaType) },
                });
            }

            blocks.Add(new BetaTextBlockParam { Text = String.IsNullOrWhiteSpace(text) ? (images.All(i => i.IsPdf) ? "(see the document)" : "(see the image)") : text });
            return blocks;
        }

        private static MediaType MediaTypeOf(String mediaType) => mediaType switch
        {
            "image/jpeg" => MediaType.ImageJpeg,
            "image/gif" => MediaType.ImageGif,
            "image/webp" => MediaType.ImageWebP,
            _ => MediaType.ImagePng,
        };

        /// <summary>Models without the dynamic-filtering web search (web_search_20260209): Haiku and older.</summary>
        public static Boolean WebSearchBasic(String model)
        {
            var m = (model ?? "").ToLowerInvariant();
            return m.Contains("haiku") || m.Contains("-3-") || m.Contains("-4-5") || m.Contains("-4-1") || m.Contains("-4-0") || m == "claude-opus-4" || m == "claude-sonnet-4";
        }

        /// <summary>The query of a web search from its streamed input ({"query": "…"}).</summary>
        public static String SearchQuery(String json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("query", out var q) ? (q.GetString() ?? "").Trim() : "";
            }
            catch (System.Text.Json.JsonException)
            {
                return "";
            }
        }

        public async IAsyncEnumerable<AiEvent> RunAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var key = this._apiKey();
            if (String.IsNullOrWhiteSpace(key))
            {
                yield return new AiEvent(AiEventKind.Error, "No Anthropic API key yet. Open Settings (⚙) and add one, or switch Claude to CLI.");
                yield break;
            }

            // The SDK throws from inside the stream; a channel lets us turn exceptions into events.
            var channel = Channel.CreateUnbounded<AiEvent>();
            var settings = this._settings();
            var model = ResolveModel(request.Model, settings.ClaudeApiModel);
            _ = Task.Run(() => this.PumpAsync(key.Trim(), model, String.IsNullOrWhiteSpace(request.Effort) ? settings.ClaudeApiEffort : request.Effort, request, channel.Writer, cancellationToken));

            yield return new AiEvent(AiEventKind.Status, "Waiting for the answer…");
            await foreach (var ev in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                yield return ev;
            }
        }

        private async Task PumpAsync(String key, String model, String effort, AiRequest request, ChannelWriter<AiEvent> writer, CancellationToken cancellationToken)
        {
            var text = new System.Text.StringBuilder();
            String stopReason = null;
            try
            {
                var client = this._baseUrl == null
                    ? new AnthropicClient { ApiKey = key }
                    : new AnthropicClient { ApiKey = key, BaseUrl = this._baseUrl };

                var parameters = new MessageCreateParams
                {
                    Model = model,
                    MaxTokens = 64000,
                    Betas = ["server-side-fallback-2026-07-01"],
                    Fallbacks = new Default(),
                    OutputConfig = new BetaOutputConfig { Effort = effort },

                    // Automatic prompt caching: the history only grows at the end, so earlier turns are
                    // read from the cache on follow-ups (short conversations are below the minimum and
                    // simply aren't cached).
                    CacheControl = new BetaCacheControlEphemeral(),
                    Messages = BuildMessages(request),
                };
                if (!String.IsNullOrWhiteSpace(request.SystemPrompt))
                {
                    parameters = parameters with { System = request.SystemPrompt };
                }

                // The agent's web search: Anthropic's server-side tool (billed per search). The dynamic-filtering
                // version on the models that have it, the basic one elsewhere (Haiku). At most five searches a turn,
                // which also keeps the server's loop short of a pause ("pause_turn").
                if (request.WebSearch)
                {
                    BetaToolUnion tool = WebSearchBasic(model)
                        ? new BetaWebSearchTool20250305 { MaxUses = 5 }
                        : new BetaWebSearchTool20260209 { MaxUses = 5 };
                    parameters = parameters with { Tools = [tool] };
                }

                var searches = new List<String>();
                var query = new System.Text.StringBuilder();
                var inSearch = false;
                var sources = new List<(String Title, String Url)>();

                await foreach (var ev in client.Beta.Messages.CreateStreaming(parameters, cancellationToken).ConfigureAwait(false))
                {
                    if (ev.TryPickContentBlockDelta(out var blockDelta) && blockDelta.Delta.TryPickText(out var piece))
                    {
                        text.Append(piece.Text);
                        await writer.WriteAsync(new AiEvent(AiEventKind.Delta, piece.Text), CancellationToken.None).ConfigureAwait(false);
                    }
                    else if (ev.TryPickContentBlockStart(out var start) && start.ContentBlock.TryPickBetaServerToolUse(out var serverTool)
                        && serverTool.Name.ToString().Contains("search", StringComparison.OrdinalIgnoreCase))
                    {
                        // A web search starts; its query arrives in pieces.
                        inSearch = true;
                        query.Clear();
                        await writer.WriteAsync(new AiEvent(AiEventKind.Status, "Searching the web…"), CancellationToken.None).ConfigureAwait(false);
                    }
                    else if (inSearch && ev.TryPickContentBlockDelta(out var jsonDelta) && jsonDelta.Delta.TryPickInputJson(out var partial))
                    {
                        query.Append(partial.PartialJson);
                    }
                    else if (inSearch && ev.TryPickContentBlockStop(out _))
                    {
                        inSearch = false;
                        var q = SearchQuery(query.ToString());
                        if (q.Length > 0)
                        {
                            searches.Add(q);
                            await writer.WriteAsync(new AiEvent(AiEventKind.Status, "Searching the web: " + q), CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    else if (ev.TryPickContentBlockStart(out var resultStart) && resultStart.ContentBlock.TryPickBetaWebSearchToolResult(out var result))
                    {
                        // The pages found (an error comes as an object instead of a list: nothing to list then).
                        if (result.Content.TryPickBetaWebSearchResultBlocks(out var found))
                        {
                            foreach (var r in found.Take(5))
                            {
                                if (!sources.Any(x => x.Url == r.Url))
                                {
                                    sources.Add((r.Title, r.Url));
                                }
                            }
                        }

                        await writer.WriteAsync(new AiEvent(AiEventKind.Status, "Reading what it found…"), CancellationToken.None).ConfigureAwait(false);
                    }
                    else if (ev.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason != null)
                    {
                        stopReason = messageDelta.Delta.StopReason.ToString();
                    }
                }

                if (stopReason != null && stopReason.Contains("refusal", StringComparison.OrdinalIgnoreCase) && text.Length == 0)
                {
                    await writer.WriteAsync(new AiEvent(AiEventKind.Error, "Claude declined this request."), CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    // What was searched and where the answer comes from, as links under it.
                    if (sources.Count > 0)
                    {
                        var list = new System.Text.StringBuilder("\n\n**Sources**\n");
                        foreach (var (title, url) in sources.Take(8))
                        {
                            list.Append("- [").Append(String.IsNullOrWhiteSpace(title) ? url : title.Replace("[", "(").Replace("]", ")")).Append("](").Append(url).Append(")\n");
                        }

                        text.Append(list);
                        await writer.WriteAsync(new AiEvent(AiEventKind.Delta, list.ToString()), CancellationToken.None).ConfigureAwait(false);
                    }

                    await writer.WriteAsync(new AiEvent(AiEventKind.Final, text.ToString()), CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                await writer.WriteAsync(new AiEvent(AiEventKind.Error, "Cancelled."), CancellationToken.None).ConfigureAwait(false);
            }
            catch (AnthropicUnauthorizedException)
            {
                await writer.WriteAsync(new AiEvent(AiEventKind.Error, "The Anthropic API key was rejected. Check it in Settings (⚙)."), CancellationToken.None).ConfigureAwait(false);
            }
            catch (AnthropicNotFoundException)
            {
                await writer.WriteAsync(new AiEvent(AiEventKind.Error, $"Model \"{model}\" was not found. Pick another model in Settings (⚙) or in the preset."), CancellationToken.None).ConfigureAwait(false);
            }
            catch (AnthropicRateLimitException)
            {
                await writer.WriteAsync(new AiEvent(AiEventKind.Error, "Anthropic API rate limit reached. Try again in a moment."), CancellationToken.None).ConfigureAwait(false);
            }
            catch (AnthropicApiException ex)
            {
                await writer.WriteAsync(new AiEvent(AiEventKind.Error, "Anthropic API error: " + ex.Message), CancellationToken.None).ConfigureAwait(false);
            }
            catch (AnthropicIOException ex)
            {
                await writer.WriteAsync(new AiEvent(AiEventKind.Error, "Could not reach the Anthropic API: " + ex.Message), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await writer.WriteAsync(new AiEvent(AiEventKind.Error, "Anthropic API request failed: " + ex.Message), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                writer.TryComplete();
            }
        }
    }
}
