using System.Globalization;
using System.IO;
using System.Linq;
using DesignChecks.Impl.Drc;
using DesignChecks.Impl.Erc;
using DXP; // extension helpers such as OpenDocument
using PCB; // extension helpers such as GetPCBBoardByPath

namespace DesignChecks.Impl.Commands
{
    internal static class DrcCommands
    {
        public const string Source = "Decoupling DRC";

        [HotCommand("DRC", "Run the decoupling-placement DRC on the open board (NoPanel: terminal only)")]
        public static string Drc(CommandContext ctx)
        {
            var board = BoardModel.Read(PcbSession.RequireBoard());
            // Which pads are supply pins, and which capacitors decouple them, comes from the schematic.
            var schematic = DesignModel.Read(AltiumSession.RequireCompiledProject());
            var config = CheckConfig.ForProject(schematic.ProjectPath, out var configPath);
            var violations = DecouplingPlacementCheck.Run(board, schematic, config).ToList();
            if (!ctx.Flag("NoPanel"))
                Report.Publish(Source, board.BoardPath, violations);
            return Report.ToText(Source, board.BoardPath, configPath, violations);
        }

        [HotCommand("PCB.Snapshot", "Save the open board's model as JSON for offline tests (Path=...)")]
        public static string Snapshot(CommandContext ctx)
        {
            var model = BoardModel.Read(PcbSession.RequireBoard());
            var path = Snapshots.Save(model, ctx.Get("Path"), Path.GetFileNameWithoutExtension(model.BoardPath) + ".pcb.snapshot.json");
            return $"PCB snapshot: {path} ({model.Pads.Count} pads)";
        }

        /// <summary>Messages-panel double-click target for DRC findings.</summary>
        [HotCommand("PCB.Navigate", "Show a board and zoom to a location (Board=path|X=mm|Y=mm|R=mm, relative to origin)")]
        public static string Navigate(CommandContext ctx)
        {
            var path = ctx.Get("Board") ?? throw new UserException("Board=<path> is required.");
            double Mm(string key, double fallback) =>
                double.TryParse(ctx.Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
            double x = Mm("X", 0), y = Mm("Y", 0), r = Mm("R", 2);

            var client = DXP.GlobalVars.Client;
            client.ShowDocument(client.OpenDocument("PCB", path) ?? throw new UserException($"Cannot open {path}."));
            var board = PCB.GlobalVars.PCBServer.GetPCBBoardByPath(path) ?? throw new UserException($"{path} is not an open PCB.");
            int ox = board.GetState_XOrigin(), oy = board.GetState_YOrigin();
            board.GraphicalView_ZoomOnRect(
                ox + PcbSession.Coord(x - r), oy + PcbSession.Coord(y - r),
                ox + PcbSession.Coord(x + r), oy + PcbSession.Coord(y + r));
            return $"Zoomed to ({x}, {y}) mm, radius {r} mm on {Path.GetFileName(path)}";
        }
    }
}
