using System.Collections.Generic;
using System.IO;
using System.Linq;
using DXP;
using EDP;
using PCB;

namespace BoardTools.Impl.Commands
{
    internal static class BoardSummaryCommand
    {
        private const string Source = "BoardTools";

        [HotCommand("BoardSummary", "Count components, nets, tracks and vias on the open board; post to Messages")]
        public static string BoardSummary(CommandContext ctx)
        {
            // Re-query on every call; never cache Altium COM objects across commands.
            var board = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard()
                        ?? throw new UserException("No PCB is open. Open a .PcbDoc and focus it first.");

            var counts = new Dictionary<string, int>
            {
                ["components"] = Count(board, TObjectId.eComponentObject),
                ["nets"] = Count(board, TObjectId.eNetObject),
                ["tracks"] = Count(board, TObjectId.eTrackObject),
                ["vias"] = Count(board, TObjectId.eViaObject),
                ["pads"] = Count(board, TObjectId.ePadObject),
                ["arcs"] = Count(board, TObjectId.eArcObject),
            };
            var name = Path.GetFileName(board.GetState_FileName());
            var text = $"{name}: " + string.Join(", ", counts.Select(c => $"{c.Key}: {c.Value}"));

            if (!ctx.Flag("NoPanel"))
                PostToMessages(name, text);
            return text;
        }

        private static int Count(IPCB_Board board, TObjectId kind)
        {
            var iterator = board.BoardIterator_Create();
            try
            {
                var set = new TObjectSet();
                set.Add(kind);
                iterator.AddFilter_ObjectSet(set);
                iterator.AddFilter_AllLayers();
                var n = 0;
                for (var o = iterator.FirstPCBObject(); o != null; o = iterator.NextPCBObject())
                    n++;
                return n;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
        }

        private static void PostToMessages(string document, string text)
        {
            var workspace = DXP.GlobalVars.DXPWorkSpace as IWorkspace ?? throw new UserException("The Altium workspace is not available.");
            var messages = workspace.DM_MessagesManager();
            messages.ClearMessagesOfClass($"[{Source} Info]");
            messages.AddMessage($"[{Source} Info]", text, Source, document, "", "",
                DXPConstant.ImageIndexErrorLevels[(int)TErrorLevel.eErrorLevelNoReport], false, "", "");
            workspace.DM_ShowMessageView();
        }
    }
}
