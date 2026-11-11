using System;
using System.Collections.Generic;
using System.Text;

namespace DesignChecks.Impl
{
    /// <summary>Everything a hot command gets from the host.</summary>
    public sealed class CommandContext
    {
        private readonly Action<string> _log;

        public CommandContext(string command, string parameters, IDictionary<string, object> env)
        {
            Command = command;
            RawParameters = parameters ?? string.Empty;
            Parameters = ParseParameters(RawParameters);
            Origin = env != null && env.TryGetValue("Origin", out var origin) ? origin as string : "UI";
            View = env != null && env.TryGetValue("Context", out var view) ? view : null;
            Generation = env != null && env.TryGetValue("Generation", out var generation) && generation is int g ? g : 0;
            _log = env != null && env.TryGetValue("Log", out var log) ? log as Action<string> : null;
        }

        public string Command { get; }
        public string RawParameters { get; }
        public IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary>"UI" (AD menu or process, panel, outputer) or "Pipe" (tools\ad.ps1); OnLoad gets "Host".</summary>
        public string Origin { get; }

        /// <summary>True when a user clicked something in AD - it is OK to show dialogs.</summary>
        public bool IsInteractive => !string.Equals(Origin, "Pipe", StringComparison.OrdinalIgnoreCase);

        /// <summary>The IServerDocumentView AD passed to the process (may be null).</summary>
        public object View { get; }

        public int Generation { get; }

        /// <summary>Streams a line to the pipe client (and the host log for UI invocations).</summary>
        public void Log(string message) => _log?.Invoke(message);

        public string Get(string name, string defaultValue = null) =>
            Parameters.TryGetValue(name, out var value) ? value : defaultValue;

        public bool Flag(string name) =>
            Parameters.TryGetValue(name, out var value) &&
            (value.Length == 0 || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

        /// <summary>Altium style "A=1|B=2"; a bare token is a flag with empty value.</summary>
        public static Dictionary<string, string> ParseParameters(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (text ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = part.IndexOf('=');
                if (eq < 0)
                    result[part] = string.Empty;
                else
                    result[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
            }
            return result;
        }

        public static string FormatParameters(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            var sb = new StringBuilder();
            foreach (var p in parameters)
            {
                if (sb.Length > 0)
                    sb.Append('|');
                sb.Append(p.Key).Append('=').Append(p.Value);
            }
            return sb.ToString();
        }
    }
}
