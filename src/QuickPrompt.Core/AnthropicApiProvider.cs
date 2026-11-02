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
                blocks.Add(new BetaImageBlockParam
                {
                    Source = new BetaBase64ImageSource { Data = image.Data, MediaType = MediaTypeOf(image.MediaType) },
                });
            }

            blocks.Add(new BetaTextBlockParam { Text = String.IsNullOrWhiteSpace(text) ? "(see the image)" : text });
            return blocks;
        }

        private static MediaType MediaTypeOf(String mediaType) => mediaType switch
        {
            "image/jpeg" => MediaType.ImageJpeg,
            "image/gif" => MediaType.ImageGif,
            "image/webp" => MediaType.ImageWebP,
            _ => MediaType.ImagePng,
        };

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
            _ = Task.Run(() => this.PumpAsync(key.Trim(), model, settings.ClaudeApiEffort, request, channel.Writer, cancellationToken));

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

                await foreach (var ev in client.Beta.Messages.CreateStreaming(parameters, cancellationToken).ConfigureAwait(false))
                {
                    if (ev.TryPickContentBlockDelta(out var blockDelta) && blockDelta.Delta.TryPickText(out var piece))
                    {
                        text.Append(piece.Text);
                        await writer.WriteAsync(new AiEvent(AiEventKind.Delta, piece.Text), CancellationToken.None).ConfigureAwait(false);
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
