using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP;

namespace DesignChecks.Impl
{
    public enum Severity { Warning, Error }

    /// <summary>A schematic object to cross-probe to ("Component" or "Net").</summary>
    public sealed record ObjectRef(string Kind, string Name, string Document)
    {
        public static ObjectRef Net(string name, string document) => new("Net", name, document);
    }

    /// <summary>A board location in mm relative to the board origin, with a zoom radius.</summary>
    public sealed record BoardLocation(string Board, double X, double Y, double Radius);

    /// <summary>One finding. Double-clicking it in the Messages panel cross-probes to Target or zooms to Location.</summary>
    public sealed record Violation(string RuleId, Severity Severity, string Message, ObjectRef Target = null, BoardLocation Location = null);

    /// <summary>Prints violations as text (for the pipe/terminal) and publishes them to AD's Messages panel.</summary>
    public static class Report
    {
        public static string ToText(string source, string target, string configPath, IReadOnlyList<Violation> violations)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{source}: {target}");
            sb.AppendLine($"Config: {configPath ?? "(defaults)"}");
            sb.AppendLine($"Result: {Summary(violations)}");
            foreach (var rule in violations.GroupBy(v => v.RuleId))
            {
                sb.AppendLine();
                sb.AppendLine($"[{rule.First().Severity}] {rule.Key} ({rule.Count()})");
                foreach (var v in rule)
                    sb.AppendLine("  " + v.Message);
            }
            return sb.ToString();
        }

        /// <summary>Replaces the previous messages of <paramref name="source"/> ("Decoupling ERC" or "Decoupling DRC"), leaving the other source's alone.</summary>
        internal static void Publish(string source, string documentPath, IReadOnlyList<Violation> violations)
        {
            var messages = AltiumSession.Messages;
            Clear(source);
            messages.BeginUpdate();
            try
            {
                foreach (var v in violations)
                {
                    var level = v.Severity == Severity.Error ? TErrorLevel.eErrorLevelError : TErrorLevel.eErrorLevelWarning;
                    var (parameters, document) = Callback(v, documentPath);
                    messages.AddMessage($"[{source} {v.Severity}]", $"[{v.RuleId}] {v.Message}", source, document,
                        parameters == null ? "" : "DesignChecks:Run", parameters ?? "",
                        DXPConstant.ImageIndexErrorLevels[(int)level], false, "", "");
                }
                messages.AddMessage($"[{source} Info]", $"{source} finished: {Summary(violations)}.", source, Path.GetFileName(documentPath),
                    "", "", DXPConstant.ImageIndexErrorLevels[(int)TErrorLevel.eErrorLevelNoReport], false, "", "");
            }
            finally
            {
                messages.EndUpdate();
            }
            AltiumSession.Workspace.DM_ShowMessageView();
        }

        internal static void Clear(string source)
        {
            foreach (var kind in new[] { "Error", "Warning", "Info" })
                AltiumSession.Messages.ClearMessagesOfClass($"[{source} {kind}]");
        }

        /// <summary>'|' separates Altium process parameters, so it is escaped in names.</summary>
        public static string Escape(string value) => value?.Replace("|", "%7C");

        public static string Unescape(string value) => value?.Replace("%7C", "|");

        private static string Summary(IReadOnlyList<Violation> violations) =>
            $"{violations.Count(v => v.Severity == Severity.Error)} error(s), {violations.Count(v => v.Severity == Severity.Warning)} warning(s)";

        /// <summary>The "DesignChecks:Run" parameters a double-click runs (null for none), and the document column.</summary>
        private static (string Parameters, string Document) Callback(Violation v, string documentPath)
        {
            if (v.Location is { } at)
            {
                var c = CultureInfo.InvariantCulture;
                return ($"Cmd=PCB.Navigate|Board={at.Board}|X={at.X.ToString(c)}|Y={at.Y.ToString(c)}|R={at.Radius.ToString(c)}",
                    Path.GetFileName(at.Board));
            }
            if (v.Target is { } target)
                return ($"Cmd=Navigate|Kind={target.Kind}|Name={Escape(target.Name)}|Project={documentPath}",
                    Path.GetFileName(target.Document ?? documentPath));
            return (null, Path.GetFileName(documentPath));
        }
    }
}
