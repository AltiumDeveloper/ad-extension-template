using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EDP;

namespace DesignChecks.Impl.Erc
{
    /// <summary>Mirror of EDP.TPinElectrical, so checks and tests do not depend on Altium types.</summary>
    public enum PinType { Input, IO, Output, OpenCollector, Passive, HiZ, OpenEmitter, Power }

    /// <summary>
    /// Plain snapshot of a compiled (flattened) project. Read once from Altium's COM objects, then
    /// checks run as pure functions over it: fast, and testable without AD (see the Snapshot command).
    /// </summary>
    public sealed class DesignModel
    {
        public string ProjectPath { get; set; }
        public List<ComponentInfo> Components { get; set; } = new();
        public List<NetInfo> Nets { get; set; } = new();

        /// <summary>Rebuilds the object links (pin ↔ component ↔ net) after construction or JSON loading.</summary>
        public DesignModel Link()
        {
            var nets = Nets.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var net in Nets)
                net.Pins.Clear();
            foreach (var component in Components)
            {
                foreach (var pin in component.Pins)
                {
                    pin.Component = component;
                    pin.Net = pin.NetName != null && nets.TryGetValue(pin.NetName, out var net) ? net : null;
                    pin.Net?.Pins.Add(pin);
                }
            }
            return this;
        }

        /// <summary>Reads the flattened document of a compiled project (UI thread only).</summary>
        internal static DesignModel Read(IProject project)
        {
            var flat = project.DM_DocumentFlattened()
                       ?? throw new UserException("The project has no flattened document. Compile it first (Project > Validate PCB Project).");
            var model = new DesignModel { ProjectPath = project.DM_ProjectFullPath() };
            var pins = new Dictionary<(string Designator, string Number), PinInfo>();

            for (var i = 0; i < flat.DM_ComponentCount(); i++)
            {
                var c = flat.DM_Components(i);
                var component = new ComponentInfo { Designator = c.DM_PhysicalDesignator(), Document = c.DM_OwnerDocumentFullPath() };
                if (model.Components.Any(x => x.Designator == component.Designator))
                    continue; // duplicate designators are reported by AD's own ERC

                // A multi-part component's pins are spread over its sub-parts; shared pins appear once per part.
                var parts = c.DM_SubPartCount() > 0 ? Enumerable.Range(0, c.DM_SubPartCount()).Select(c.DM_SubParts) : new IPart[] { c };
                foreach (var part in parts.Where(p => p != null))
                {
                    for (var p = 0; p < part.DM_PinCount(); p++)
                    {
                        var pin = part.DM_Pins(p);
                        var info = new PinInfo { Number = pin.DM_PinNumber(), Name = pin.DM_PinName(), Type = (PinType)(int)pin.DM_Electrical() };
                        if (pins.TryAdd((component.Designator, info.Number), info))
                            component.Pins.Add(info);
                    }
                }
                model.Components.Add(component);
            }

            for (var i = 0; i < flat.DM_NetCount(); i++)
            {
                var net = flat.DM_Nets(i);
                var name = net.DM_FullNetName();
                if (string.IsNullOrEmpty(name))
                    name = net.DM_CalculatedNetName();
                if (model.Nets.Any(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)))
                    name = $"{name}#{i}";
                model.Nets.Add(new NetInfo { Name = name, SuppressErc = net.DM_SuppressERC() });

                for (var p = 0; p < net.DM_PinCount(); p++)
                {
                    var pin = net.DM_Pins(p);
                    if (pins.TryGetValue((pin.DM_PhysicalPartDesignator(), pin.DM_PinNumber()), out var info))
                        info.NetName = name;
                }
            }
            return model.Link();
        }
    }

    public sealed class ComponentInfo
    {
        public string Designator { get; set; }

        /// <summary>The schematic sheet the component is on (where cross-probing opens).</summary>
        public string Document { get; set; }

        public List<PinInfo> Pins { get; set; } = new();

        [JsonIgnore] public bool IsIc => Prefix is "U" or "IC";
        [JsonIgnore] public bool IsCapacitor => Prefix is "C";
        [JsonIgnore] public bool IsInductor => Prefix is "L";

        /// <summary>Letters before the first digit: "U12" → "U".</summary>
        private string Prefix => new string((Designator ?? "").TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();

        /// <summary>True when the component has a pin on <paramref name="net"/> and another on a net matching <paramref name="other"/>.</summary>
        public bool Bridges(NetInfo net, Func<NetInfo, bool> other) =>
            Pins.Any(p => p.Net == net) && Pins.Any(p => p.Net != null && p.Net != net && other(p.Net));

        public override string ToString() => Designator;
    }

    public sealed class PinInfo
    {
        private static readonly Regex SupplyName = new(
            @"^(VCC|VDD|AVDD|DVDD|PVDD|VDDA|VDDD|VDDIO|VCCIO|VDDQ|VCCA|VCCB|VIN|VBAT|VBUS|VS|V\+|VCORE|VDDCORE|VREG|VPP)(\d+|_\w+)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string Number { get; set; }
        public string Name { get; set; }
        public PinType Type { get; set; }

        /// <summary>Net name, or null when the pin is unconnected.</summary>
        public string NetName { get; set; }

        [JsonIgnore] public ComponentInfo Component { get; set; }
        [JsonIgnore] public NetInfo Net { get; set; }

        /// <summary>
        /// A supply input: a Power pin, or a supply-named one (libraries often mark VCC as Passive),
        /// on a net that is not ground (so GND/VSS pins never count).
        /// </summary>
        [JsonIgnore]
        public bool IsSupply => Net != null && !Net.IsGround && (Type == PinType.Power || SupplyName.IsMatch(Name?.Trim() ?? ""));

        public override string ToString() => $"{Component}-{Number}";
    }

    public sealed class NetInfo
    {
        private static readonly Regex GroundName = new(
            @"^((A|D|P|S|C|E|SIG|PWR)_?)?GND(_?\w+)?$|^VSS\w*$|^0V$|^GROUND$|^EARTH$|^CHASSIS\w*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string Name { get; set; }

        /// <summary>The net carries a No ERC directive: skip it.</summary>
        public bool SuppressErc { get; set; }

        [JsonIgnore] public List<PinInfo> Pins { get; } = new();
        [JsonIgnore] public IEnumerable<ComponentInfo> Components => Pins.Select(p => p.Component).Distinct();

        /// <summary>Ground by name; the sheet prefix of a local net ("Sheet/GND") is ignored.</summary>
        [JsonIgnore] public bool IsGround => GroundName.IsMatch(Name[(Name.LastIndexOfAny(new[] { '/', '\\' }) + 1)..]);

        public override string ToString() => Name;
    }
}
