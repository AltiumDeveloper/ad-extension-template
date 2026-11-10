using System;
using System.Collections.Generic;
using System.Linq;
using PCB;

namespace DesignChecks.Impl.Drc
{
    /// <summary>
    /// Plain snapshot of a PCB document: the pads, which is all the decoupling DRC needs. Lengths are
    /// mm, positions relative to the board origin shown in the editor.
    /// </summary>
    public sealed class BoardModel
    {
        public string BoardPath { get; set; }
        public List<PadInfo> Pads { get; set; } = new();

        /// <summary>Reads an open board (UI thread only).</summary>
        internal static BoardModel Read(IPCB_Board board)
        {
            int ox = board.GetState_XOrigin(), oy = board.GetState_YOrigin();
            var model = new BoardModel { BoardPath = board.GetState_FileName() };
            foreach (IPCB_Pad p in PcbSession.Objects(board, TObjectId.ePadObject))
            {
                var bottom = p.GetState_Layer() == TV6_Layer.eV6_BottomLayer;
                model.Pads.Add(new PadInfo
                {
                    Designator = p.GetState_Component()?.GetState_Name()?.GetState_Text(),
                    Name = p.GetState_Name(),
                    X = PcbSession.Mm(p.GetState_XLocation() - ox),
                    Y = PcbSession.Mm(p.GetState_YLocation() - oy),
                    SizeX = PcbSession.Mm(bottom ? p.GetState_BotXSize() : p.GetState_TopXSize()),
                    SizeY = PcbSession.Mm(bottom ? p.GetState_BotYSize() : p.GetState_TopYSize()),
                    Rotation = p.GetState_Rotation(),
                });
            }
            return model;
        }
    }

    /// <summary>A component pad, treated as a (rotated) rectangle for distances.</summary>
    public sealed class PadInfo
    {
        /// <summary>Owning component, matched to the schematic by designator.</summary>
        public string Designator { get; set; }

        /// <summary>Pad name, matched to the schematic pin number.</summary>
        public string Name { get; set; }

        public double X { get; set; }
        public double Y { get; set; }
        public double SizeX { get; set; }
        public double SizeY { get; set; }
        public double Rotation { get; set; }

        /// <summary>Edge-to-edge distance between two pads (0 when they overlap).</summary>
        public double DistanceTo(PadInfo other)
        {
            if (Contains(other.X, other.Y) || other.Contains(X, Y))
                return 0;
            // For two convex outlines the minimum distance is from a corner of one to the other.
            return Math.Min(other.Corners().Min(c => DistanceTo(c.X, c.Y)), Corners().Min(c => other.DistanceTo(c.X, c.Y)));
        }

        /// <summary>Distance from a point to the pad outline (0 inside).</summary>
        public double DistanceTo(double x, double y)
        {
            var (lx, ly) = ToLocal(x, y);
            double dx = Math.Max(Math.Abs(lx) - SizeX / 2, 0), dy = Math.Max(Math.Abs(ly) - SizeY / 2, 0);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private bool Contains(double x, double y)
        {
            var (lx, ly) = ToLocal(x, y);
            return Math.Abs(lx) <= SizeX / 2 && Math.Abs(ly) <= SizeY / 2;
        }

        private IEnumerable<(double X, double Y)> Corners()
        {
            var a = Rotation * Math.PI / 180;
            double c = Math.Cos(a), s = Math.Sin(a), hx = SizeX / 2, hy = SizeY / 2;
            foreach (var (lx, ly) in new[] { (-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy) })
                yield return (X + lx * c - ly * s, Y + lx * s + ly * c);
        }

        private (double X, double Y) ToLocal(double x, double y)
        {
            var a = -Rotation * Math.PI / 180;
            double dx = x - X, dy = y - Y;
            return (dx * Math.Cos(a) - dy * Math.Sin(a), dx * Math.Sin(a) + dy * Math.Cos(a));
        }

        public override string ToString() => $"{Designator}-{Name}";
    }
}
