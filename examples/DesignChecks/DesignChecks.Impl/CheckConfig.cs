using System.IO;
using System.Text.Json;

namespace DesignChecks.Impl
{
    /// <summary>
    /// Check thresholds. Defaults live here; a project can override them with a "DesignChecks.json" next
    /// to its .PrjPcb, for example <c>{ "MaxDecouplingDistanceMm": 5 }</c>.
    /// </summary>
    public sealed class CheckConfig
    {
        public const string FileName = "DesignChecks.json";

        /// <summary>ERC: decoupling capacitors expected per IC on a supply node.</summary>
        public double DecouplingCapsPerIc { get; set; } = 1.0;

        /// <summary>DRC: max distance from an IC supply pad to its nearest decoupling capacitor, pad edge to pad edge.</summary>
        public double MaxDecouplingDistanceMm { get; set; } = 3.0;

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>The project's DesignChecks.json if there is one (its path in <paramref name="path"/>), else the defaults (null path).</summary>
        public static CheckConfig ForProject(string projectPath, out string path)
        {
            path = Path.Combine(Path.GetDirectoryName(projectPath) ?? "", FileName);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<CheckConfig>(File.ReadAllText(path), Json) ?? new CheckConfig();
            path = null;
            return new CheckConfig();
        }
    }
}
