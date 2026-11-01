namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>A line-oriented parser for a CLI's JSONL output.</summary>
    public interface ICliStreamParser
    {
        IEnumerable<AiEvent> Feed(String line);

        /// <summary>Called after the process exits. Must yield the closing Final/Error event if Feed has not.</summary>
        IEnumerable<AiEvent> Complete(Int32 exitCode, String stderr, Boolean cancelled);

        Boolean IsFinished { get; }
    }

    public sealed class CliInvocation
    {
        public CliInvocation(IReadOnlyList<String> arguments, String stdin, ICliStreamParser parser)
        {
            this.Arguments = arguments;
            this.Stdin = stdin ?? "";
            this.Parser = parser;
        }

        public IReadOnlyList<String> Arguments { get; }

        public String Stdin { get; }

        public ICliStreamParser Parser { get; }
    }

    /// <summary>Runs a CLI in the background (no window), feeds the prompt on stdin and parses stdout.</summary>
    public abstract class CliProviderBase : IAiProvider
    {
        private readonly Func<QuickPromptSettings> _settings;
        private readonly String _workingDirectory;

        protected CliProviderBase(Func<QuickPromptSettings> settings, String workingDirectory)
        {
            this._settings = settings;
            this._workingDirectory = workingDirectory;
        }

        public abstract String Id { get; }

        public abstract String DisplayName { get; }

        protected abstract String ExecutableName { get; }

        protected abstract String InstallHint { get; }

        protected QuickPromptSettings Settings => this._settings();

        protected String WorkingDirectory => this._workingDirectory;

        protected abstract String GetOverridePath(QuickPromptSettings settings);

        /// <summary>Arguments, stdin text and a fresh parser for one run.</summary>
        protected abstract CliInvocation CreateInvocation(AiRequest request);

        public String LocateExecutable() => CliLocator.Find(this.ExecutableName, this.GetOverridePath(this.Settings));

        public async IAsyncEnumerable<AiEvent> RunAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var executable = this.LocateExecutable();
            if (executable == null)
            {
                yield return new AiEvent(AiEventKind.Error, this.InstallHint);
                yield break;
            }

            var invocation = this.CreateInvocation(request);
            var (process, startError) = TryStart(executable, invocation.Arguments, this._workingDirectory);
            if (process == null)
            {
                yield return new AiEvent(AiEventKind.Error, $"Could not start {this.DisplayName}: {startError}");
                yield break;
            }

            using (process)
            {
                var parser = invocation.Parser;
                using var registration = cancellationToken.Register(() => ProcessHelpers.KillTree(process));
                var stderrTask = ProcessHelpers.ReadTailAsync(process.StandardError);
                var stdinTask = WriteStdinAsync(process, invocation.Stdin);

                yield return new AiEvent(AiEventKind.Status, "Waiting for the answer…");

                while (true)
                {
                    String line;
                    try
                    {
                        line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
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
                }

                await stdinTask.ConfigureAwait(false);
                try
                {
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Ignore.
                }

                var stderr = await stderrTask.ConfigureAwait(false);
                var exitCode = SafeExitCode(process);

                foreach (var ev in parser.Complete(exitCode, stderr, cancellationToken.IsCancellationRequested))
                {
                    yield return ev;
                }
            }
        }

        private static (Process, String) TryStart(String executable, IReadOnlyList<String> arguments, String workingDirectory)
        {
            try
            {
                var psi = ProcessHelpers.CreateStartInfo(executable, arguments, workingDirectory, CliLocator.GetEnrichedPath());
                var process = Process.Start(psi);
                return process == null ? (null, "process could not be created") : (process, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        private static async Task WriteStdinAsync(Process process, String text)
        {
            try
            {
                await process.StandardInput.WriteAsync(text).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Process exited early; the parser reports the error.
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (Exception)
                {
                    // Ignore.
                }
            }
        }

        private static Int32 SafeExitCode(Process process)
        {
            try
            {
                return process.HasExited ? process.ExitCode : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
