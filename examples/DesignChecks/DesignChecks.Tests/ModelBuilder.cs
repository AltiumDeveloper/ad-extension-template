using System.Linq;
using DesignChecks.Impl.Erc;

namespace DesignChecks.Tests
{
    /// <summary>
    /// Tiny DSL for synthetic schematics:
    /// <code>new ModelBuilder().Part("U1", ("1", "VDD", PinType.Power, "3V3")).Two("C1", "3V3", "GND").Build()</code>
    /// Nets are created on first use.
    /// </summary>
    internal sealed class ModelBuilder
    {
        private readonly DesignModel _model = new() { ProjectPath = @"C:\test\Test.PrjPcb" };

        public ModelBuilder Part(string designator, params (string Number, string Name, PinType Type, string Net)[] pins)
        {
            foreach (var net in pins.Select(p => p.Net).Where(n => n != null && _model.Nets.All(x => x.Name != n)))
                _model.Nets.Add(new NetInfo { Name = net });

            var component = new ComponentInfo { Designator = designator, Document = "Sheet1.SchDoc" };
            component.Pins.AddRange(pins.Select(p => new PinInfo { Number = p.Number, Name = p.Name, Type = p.Type, NetName = p.Net }));
            _model.Components.Add(component);
            return this;
        }

        /// <summary>Two-pin passive between two nets.</summary>
        public ModelBuilder Two(string designator, string netA, string netB) =>
            Part(designator, ("1", "1", PinType.Passive, netA), ("2", "2", PinType.Passive, netB));

        public ModelBuilder NoErc(string net)
        {
            _model.Nets.Single(n => n.Name == net).SuppressErc = true;
            return this;
        }

        public DesignModel Build() => _model.Link();
    }
}
