using System;
using System.Collections.Generic;
using EDP;
using PCB;

namespace DesignChecks.Impl
{
    /// <summary>Helpers over the PCB server (UI thread only; never cache PCB objects across commands).</summary>
    internal static class PcbSession
    {
        /// <summary>The board in the current view, else the open PCB of the focused project.</summary>
        public static IPCB_Board RequireBoard()
        {
            var server = PCB.GlobalVars.PCBServer ?? throw new UserException("The PCB server is not running.");
            var board = server.GetCurrentPCBBoard();
            if (board != null)
                return board;

            var project = AltiumSession.Workspace.DM_FocusedProject();
            if (project != null && !AltiumSession.IsFreeDocuments(project))
            {
                for (var i = 0; i < project.DM_PhysicalDocumentCount(); i++)
                {
                    var doc = project.DM_PhysicalDocuments(i);
                    if (string.Equals(doc.DM_DocumentKind(), "PCB", StringComparison.OrdinalIgnoreCase))
                    {
                        board = server.GetPCBBoardByPath(doc.DM_FullPath());
                        if (board != null)
                            return board;
                    }
                }
            }
            throw new UserException("No PCB document is open. Open the project's .PcbDoc first.");
        }

        /// <summary>
        /// Iterates board objects of the given kinds on all layers; the iterator is always destroyed.
        /// Filter by kind here, or branch on GetState_ObjectID(): every Altium interface is IDispatch
        /// based, so a test like 'o is IPCB_Pad' is true for any PCB object.
        /// </summary>
        public static IEnumerable<IPCB_Primitive> Objects(IPCB_Board board, params TObjectId[] kinds)
        {
            var iterator = board.BoardIterator_Create();
            try
            {
                var set = new TObjectSet();
                foreach (var kind in kinds)
                    set.Add(kind);
                iterator.AddFilter_ObjectSet(set);
                iterator.AddFilter_AllLayers();
                for (var o = iterator.FirstPCBObject(); o != null; o = iterator.NextPCBObject())
                    yield return o;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
        }

        /// <summary>Altium coordinates (1/10000 mil) to mm.</summary>
        public static double Mm(int coord) => Math.Round(coord / 10000.0 * 0.0254, 4);

        public static int Coord(double mm) => (int)Math.Round(mm / 0.0254 * 10000.0);
    }
}
