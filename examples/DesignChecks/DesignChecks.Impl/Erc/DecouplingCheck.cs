using System;
using System.Collections.Generic;
using System.Linq;

namespace DesignChecks.Impl.Erc
{
    /// <summary>A net that feeds IC supply pins, with the capacitors that connect it to ground.</summary>
    public sealed record SupplyNode(NetInfo Net, IReadOnlyList<PinInfo> SupplyPins, IReadOnlyList<ComponentInfo> Capacitors)
    {
        public IReadOnlyList<ComponentInfo> Ics => SupplyPins.Select(p => p.Component).Distinct().ToList();
    }

    /// <summary>
    /// ERC: every supply node feeding ICs needs decoupling capacitors to ground. The schematic cannot
    /// prove placement (that is the DRC's job), but a node feeding N ICs with fewer than N capacitors,
    /// or none, is almost always an omission.
    /// </summary>
    public static class DecouplingCheck
    {
        public const string Missing = "DECAP-MISSING";
        public const string Low = "DECAP-LOW";

        public static IEnumerable<Violation> Run(DesignModel model, CheckConfig config)
        {
            foreach (var node in SupplyNodes(model))
            {
                var net = node.Net;
                var ics = node.Ics;
                var caps = node.Capacitors;
                var expected = (int)Math.Ceiling(ics.Count * config.DecouplingCapsPerIc);
                var target = ObjectRef.Net(net.Name, ics[0].Document);

                // Switching-regulator node (IC pin → inductor → decoupled rail, e.g. an MCU's internal
                // DC-DC): its capacitors belong on the far side of the inductor.
                if (caps.Count == 0 && net.Components.Any(c => c.IsInductor && c.Bridges(net, _ => true)))
                    continue;

                if (caps.Count == 0)
                    yield return new Violation(Missing, Severity.Error,
                        $"Supply '{net}' feeds {Count(ics, "IC")} ({List(ics)}) but has no decoupling capacitor to ground.", target);
                else if (caps.Count < expected)
                    yield return new Violation(Low, Severity.Warning,
                        $"Supply '{net}' feeds {Count(ics, "IC")} ({List(ics)}) but has only {Count(caps, "decoupling capacitor")} " +
                        $"({List(caps)}); expected at least {expected}.", target);
            }
        }

        /// <summary>
        /// Every net (named rail or not) carrying an IC supply pin, except No-ERC nets and nets with a single pin.
        /// Shared with the DRC, so both checks agree on what needs decoupling.
        /// </summary>
        public static IEnumerable<SupplyNode> SupplyNodes(DesignModel model) =>
            model.Components
                .Where(c => c.IsIc)
                .SelectMany(c => c.Pins)
                .Where(p => p.IsSupply && !p.Net.SuppressErc && p.Net.Pins.Count > 1)
                .GroupBy(p => p.Net)
                .OrderBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SupplyNode(g.Key,
                    g.OrderBy(p => p.Component.Designator).ToList(),
                    g.Key.Components.Where(c => c.IsCapacitor && c.Bridges(g.Key, n => n.IsGround)).OrderBy(c => c.Designator).ToList()));

        private static string Count<T>(IReadOnlyCollection<T> items, string noun) => items.Count == 1 ? $"1 {noun}" : $"{items.Count} {noun}s";

        private static string List<T>(IEnumerable<T> items, int max = 6)
        {
            var all = items.Select(i => i.ToString()).ToList();
            return all.Count <= max ? string.Join(", ", all) : string.Join(", ", all.Take(max)) + $" +{all.Count - max} more";
        }
    }
}
