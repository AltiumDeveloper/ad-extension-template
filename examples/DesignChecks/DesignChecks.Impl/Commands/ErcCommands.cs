using System;
using System.IO;
using System.Linq;
using DesignChecks.Impl.Erc;
using EDP; // extension helpers such as DM_GetProjectFromPath

namespace DesignChecks.Impl.Commands
{
    internal static class ErcCommands
    {
        public const string Source = "Decoupling ERC";

        [HotCommand("ERC", "Run the decoupling ERC on the focused project (NoPanel: terminal only)")]
        public static string Erc(CommandContext ctx)
        {
            var model = DesignModel.Read(AltiumSession.RequireCompiledProject());
            var config = CheckConfig.ForProject(model.ProjectPath, out var configPath);
            var violations = DecouplingCheck.Run(model, config).ToList();
            if (!ctx.Flag("NoPanel"))
                Report.Publish(Source, model.ProjectPath, violations);
            return Report.ToText(Source, model.ProjectPath, configPath, violations);
        }

        [HotCommand("ERC.Clear", "Remove Decoupling ERC messages from the Messages panel")]
        public static string Clear(CommandContext ctx)
        {
            Report.Clear(Source);
            return "Decoupling ERC messages cleared.";
        }

        [HotCommand("Snapshot", "Save the focused project's design model as JSON for offline tests (Path=...)")]
        public static string Snapshot(CommandContext ctx)
        {
            var model = DesignModel.Read(AltiumSession.RequireCompiledProject());
            var path = Snapshots.Save(model, ctx.Get("Path"), Path.GetFileNameWithoutExtension(model.ProjectPath) + ".snapshot.json");
            return $"Snapshot: {path} ({model.Components.Count} components, {model.Nets.Count} nets)";
        }

        /// <summary>Messages-panel double-click target for ERC findings.</summary>
        [HotCommand("Navigate", "Cross-probe to a schematic object (Kind=Component|Net, Name=..., Project=...)")]
        public static string Navigate(CommandContext ctx)
        {
            var projectPath = ctx.Get("Project");
            var project = (string.IsNullOrEmpty(projectPath) ? null : AltiumSession.Workspace.DM_GetProjectFromPath(projectPath))
                          ?? AltiumSession.RequireCompiledProject();
            if (project.DM_NeedsCompile())
                project.DM_Compile();

            var flat = project.DM_DocumentFlattened();
            var name = Report.Unescape(ctx.Get("Name"));
            bool Is(string candidate) => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase);

            if (string.Equals(ctx.Get("Kind"), "Net", StringComparison.OrdinalIgnoreCase))
            {
                var net = Enumerable.Range(0, flat.DM_NetCount()).Select(flat.DM_Nets).FirstOrDefault(n => Is(n.DM_FullNetName()))
                          ?? throw new UserException($"Net '{name}' not found (recompile the project?).");
                net.DM_DoCrossProbe();
                return $"Cross-probed net {name}";
            }

            var component = Enumerable.Range(0, flat.DM_ComponentCount()).Select(flat.DM_Components).FirstOrDefault(c => Is(c.DM_PhysicalDesignator()))
                            ?? throw new UserException($"Component '{name}' not found (recompile the project?).");
            component.DM_DoCrossProbe();
            return $"Cross-probed component {name}";
        }
    }
}
