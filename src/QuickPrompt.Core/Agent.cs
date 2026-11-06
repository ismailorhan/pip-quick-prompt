namespace QuickPrompt.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A named AI setup: which AI, how it connects, which model, which of the user's plugins / MCP servers /
    /// skills it may use, and an optional instruction of its own. One agent is the default: new chats, Pip and
    /// the shortcuts use it. Prompt templates pick an agent instead of carrying these settings themselves.
    /// </summary>
    public sealed class Agent
    {
        public String Id { get; set; } = "";

        public String Name { get; set; } = "";

        /// <summary>A colour for the agent's dot ("#D97757").</summary>
        public String Color { get; set; } = "";

        /// <summary>ProviderIds: "claude" or "codex" (ChatGPT).</summary>
        public String ProviderId { get; set; } = ProviderIds.Claude;

        /// <summary>"cli" or "api".</summary>
        public String Connection { get; set; } = ConnectionModes.Cli;

        /// <summary>Empty: the CLI's own default, or the API model in Settings.</summary>
        public String Model { get; set; } = "";

        /// <summary>Anthropic API effort (low … max). Empty: as in Settings.</summary>
        public String Effort { get; set; } = "";

        /// <summary>Plugins, MCP servers and skills this agent may use through the CLI (off = plain answers).</summary>
        public CliSetupChoice Setup { get; set; } = new CliSetupChoice();

        /// <summary>API connection: the provider's own web search (Anthropic's server-side tool; billed per search). Off by default.</summary>
        public Boolean WebSearch { get; set; }

        /// <summary>Added to the system prompt for this agent.</summary>
        public String Instruction { get; set; } = "";

        public const String ClaudeColor = "#D97757";

        public const String ChatGptColor = "#10A37F";

        public static readonly String[] Palette = { "#D97757", "#10A37F", "#4C8DF6", "#A56BF2", "#E5484D", "#F5A524", "#2BB5C8", "#8E8E93" };

        public static readonly String[] Efforts = { "low", "medium", "high", "xhigh", "max" };

        public Agent Clone() => new Agent
        {
            Id = this.Id,
            Name = this.Name,
            Color = this.Color,
            ProviderId = this.ProviderId,
            Connection = this.Connection,
            Model = this.Model,
            Effort = this.Effort,
            WebSearch = this.WebSearch,
            Setup = CloneSetup(this.Setup),
            Instruction = this.Instruction,
        };

        public static CliSetupChoice CloneSetup(CliSetupChoice c) => c == null ? new CliSetupChoice() : new CliSetupChoice
        {
            Enabled = c.Enabled,
            Skills = c.Skills,
            Web = c.Web,
            DisabledPlugins = new List<String>(c.DisabledPlugins ?? new List<String>()),
            DisabledServers = new List<String>(c.DisabledServers ?? new List<String>()),
            DisabledSkills = new List<String>(c.DisabledSkills ?? new List<String>()),
        };

        /// <summary>Puts every field in range (a hand-edited settings.json must not break a run).</summary>
        public void Normalize()
        {
            this.Id = (this.Id ?? "").Trim();
            this.Name = (this.Name ?? "").Trim();
            this.ProviderId = this.ProviderId == ProviderIds.Codex ? ProviderIds.Codex : ProviderIds.Claude;
            this.Connection = ConnectionModes.Normalize(this.Connection, ConnectionModes.Cli) == ConnectionModes.Api ? ConnectionModes.Api : ConnectionModes.Cli;
            this.Model = (this.Model ?? "").Trim();
            this.Effort = Efforts.Contains((this.Effort ?? "").Trim().ToLowerInvariant()) ? this.Effort.Trim().ToLowerInvariant() : "";
            this.Setup ??= new CliSetupChoice();
            this.Setup.DisabledPlugins ??= new List<String>();
            this.Setup.DisabledServers ??= new List<String>();
            this.Setup.DisabledSkills ??= new List<String>();
            this.Instruction = (this.Instruction ?? "").Trim();
            if (this.Instruction.Length > 4000)
            {
                this.Instruction = this.Instruction.Substring(0, 4000);
            }

            this.Color = System.Text.RegularExpressions.Regex.IsMatch(this.Color ?? "", "^#[0-9A-Fa-f]{6}$")
                ? this.Color
                : this.ProviderId == ProviderIds.Codex ? ChatGptColor : ClaudeColor;
            if (this.Name.Length == 0)
            {
                this.Name = this.ProviderId == ProviderIds.Codex ? "ChatGPT" : "Claude";
            }

            if (this.Name.Length > 40)
            {
                this.Name = this.Name.Substring(0, 40);
            }
        }

        /// <summary>The same AI, connection, model, effort, setup and instruction (names and colours aside).</summary>
        public Boolean SameSetupAs(Agent other)
        {
            if (other == null)
            {
                return false;
            }

            Boolean SameList(List<String> a, List<String> b) =>
                new HashSet<String>(a ?? new List<String>()).SetEquals(b ?? new List<String>());
            Boolean SameChoice(CliSetupChoice a, CliSetupChoice b)
            {
                a ??= new CliSetupChoice();
                b ??= new CliSetupChoice();
                if (a.Enabled != b.Enabled)
                {
                    return false;
                }

                return !a.Enabled || (a.Skills == b.Skills && a.Web == b.Web && SameList(a.DisabledPlugins, b.DisabledPlugins)
                    && SameList(a.DisabledServers, b.DisabledServers) && SameList(a.DisabledSkills, b.DisabledSkills));
            }

            return this.ProviderId == other.ProviderId
                && this.Connection == other.Connection
                && String.Equals(this.Model ?? "", other.Model ?? "", StringComparison.OrdinalIgnoreCase)
                && (this.Effort ?? "") == (other.Effort ?? "")
                && this.WebSearch == other.WebSearch
                && (this.Instruction ?? "") == (other.Instruction ?? "")
                && SameChoice(this.Setup, other.Setup);
        }

        public static String NewId() => "g" + Guid.NewGuid().ToString("N").Substring(0, 10);
    }
}
