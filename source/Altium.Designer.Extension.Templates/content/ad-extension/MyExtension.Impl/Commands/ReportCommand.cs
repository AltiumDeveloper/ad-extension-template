using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DXP;
using EDP;

namespace MyExtension.Impl.Commands
{
    internal static class ReportCommand
    {
        /// <summary>
        /// Writes the "MyExtension Report" OutJob output. The outputer in the shim passes the project as the
        /// context and the target as File=. From a terminal (tools\ad.ps1 run Report) it uses the focused
        /// project and %TEMP%, so you can iterate on the content without running the OutJob.
        /// </summary>
        [HotCommand("Report", "Write the project report of the OutJob output (File=path; default: focused project, %TEMP%)")]
        public static string Report(CommandContext ctx)
        {
            var project = AltiumSession.Compiled(ctx.View as IProject ?? AltiumSession.Workspace.DM_FocusedProject());

            var file = ctx.Get("File") ??
                       Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(project.DM_ProjectFullPath()) + ".txt");
            var lines = BuildReport(project);
            File.WriteAllLines(file, lines);
            return $"{file}: {lines.Count} lines";
        }

        private static List<string> BuildReport(IProject project)
        {
            var design = project.DM_DocumentFlattened();
            var lines = new List<string> { "Project: " + Path.GetFileName(project.DM_ProjectFullPath()) };
            if (design == null)
                return lines;

            var components = Enumerable.Range(0, design.DM_ComponentCount()).Select(design.DM_Components)
                .Select(c => (Designator: c.DM_PhysicalDesignator(), Comment: c.DM_Comment()))
                .OrderBy(c => c.Designator)
                .ToList();
            lines.Add($"Components: {components.Count}");
            lines.Add($"Nets: {design.DM_NetCount()}");
            lines.Add(string.Empty);
            lines.AddRange(components.Select(c => $"{c.Designator}\t{c.Comment}"));

            var nets = Enumerable.Range(0, design.DM_NetCount()).Select(design.DM_Nets)
                .Select(n => (Name: string.IsNullOrEmpty(n.DM_FullNetName()) ? n.DM_CalculatedNetName() : n.DM_FullNetName(),
                              Pins: Enumerable.Range(0, n.DM_PinCount()).Select(n.DM_Pins)
                                  .Select(p => p.DM_PhysicalPartDesignator() + "-" + p.DM_PinNumber())
                                  .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()))
                .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            lines.Add(string.Empty);
            lines.Add("Net\tPins\tConnections");
            lines.AddRange(nets.Select(n => $"{n.Name}\t{n.Pins.Count}\t{string.Join(" ", n.Pins)}"));
            return lines;
        }
    }
}
