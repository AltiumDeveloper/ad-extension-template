using System.Collections.Generic;
using System.Linq;
using DesignChecks.Impl.Erc;

namespace DesignChecks.Impl.Drc
{
    /// <summary>
    /// DRC: a decoupling capacitor only works when its loop to the IC is short. For every IC supply
    /// pad, the nearest capacitor on the same supply node must be within MaxDecouplingDistanceMm,
    /// pad edge to pad edge. The schematic says which pads are supply pins and which capacitors
    /// decouple them (<see cref="DecouplingCheck.SupplyNodes"/>); the board says where they are.
    /// Nodes with no capacitor at all are the ERC's DECAP-MISSING.
    /// </summary>
    public static class DecouplingPlacementCheck
    {
        public const string TooFar = "DRC-DECAP-DIST";

        public static IEnumerable<Violation> Run(BoardModel board, DesignModel schematic, CheckConfig config)
        {
            var max = config.MaxDecouplingDistanceMm;
            var pads = board.Pads.Where(p => p.Designator != null).ToLookup(p => (p.Designator, p.Name));
            IEnumerable<PadInfo> PadsOf(IEnumerable<PinInfo> pins) => pins.SelectMany(pin => pads[(pin.Component.Designator, pin.Number)]);

            var findings = new List<(string Ic, string Net, Violation Violation)>();
            foreach (var node in DecouplingCheck.SupplyNodes(schematic))
            {
                var capPads = PadsOf(node.Capacitors.SelectMany(c => c.Pins).Where(p => p.Net == node.Net)).ToList();
                if (capPads.Count == 0)
                    continue;

                foreach (var ic in node.SupplyPins.GroupBy(p => p.Component))
                {
                    var icPads = PadsOf(ic).ToList();
                    var far = icPads
                        .Select(pad => capPads.Select(cap => (Pad: pad, Cap: cap, Distance: pad.DistanceTo(cap))).MinBy(x => x.Distance))
                        .Where(x => x.Distance > max)
                        .OrderByDescending(x => x.Distance)
                        .ToList();
                    if (far.Count == 0)
                        continue;

                    var (pad, cap, distance) = far[0];
                    var what = icPads.Count == 1
                        ? $"supply pad {pad} ({node.Net})"
                        : $"{far.Count} of {icPads.Count} {node.Net} supply pads (worst {pad})";
                    findings.Add((ic.Key.Designator, node.Net.Name, new Violation(TooFar, Severity.Warning,
                        $"{ic.Key}: {what} - nearest decoupling capacitor {cap.Designator} is {distance:0.0} mm away pad-to-pad (max {max:0.0} mm).",
                        Location: new BoardLocation(board.BoardPath, (pad.X + cap.X) / 2, (pad.Y + cap.Y) / 2, distance / 2 + 1.5))));
                }
            }
            return findings.OrderBy(f => f.Ic).ThenBy(f => f.Net).Select(f => f.Violation);
        }
    }
}
