using System.Linq;
using DesignChecks.Impl;
using DesignChecks.Impl.Drc;
using DesignChecks.Impl.Erc;
using Xunit;

namespace DesignChecks.Tests
{
    public class DecouplingPlacementCheckTests
    {
        /// <summary>U1 (VDD pad at 0,0; GND pad at 0,-2) and C1 bridging 3V3 to GND, its 3V3 pad at x = capX. Pads are 0.6 mm squares.</summary>
        private static Violation[] Run(double capX, PinType u1Pin1 = PinType.Power, string u1Pin1Name = "VDD")
        {
            var schematic = new ModelBuilder()
                .Part("U1", ("1", u1Pin1Name, u1Pin1, "3V3"), ("2", "GND", PinType.Power, "GND"))
                .Two("C1", "3V3", "GND")
                .Build();
            var board = new BoardModel { BoardPath = "test.PcbDoc" };
            board.Pads.AddRange(new[] { Pad("U1", "1", 0, 0), Pad("U1", "2", 0, -2), Pad("C1", "1", capX, 0), Pad("C1", "2", capX + 1, 0) });
            return DecouplingPlacementCheck.Run(board, schematic, new CheckConfig()).ToArray();
        }

        private static PadInfo Pad(string designator, string name, double x, double y) =>
            new() { Designator = designator, Name = name, X = x, Y = y, SizeX = 0.6, SizeY = 0.6 };

        [Fact]
        public void Close_capacitor_passes()
        {
            Assert.Empty(Run(capX: 2.0)); // 1.4 mm pad edge to pad edge
        }

        [Fact]
        public void Far_capacitor_is_flagged_with_edge_distance_and_location()
        {
            var v = Assert.Single(Run(capX: 10.0));
            Assert.Equal(DecouplingPlacementCheck.TooFar, v.RuleId);
            Assert.Contains("U1: supply pad U1-1 (3V3) - nearest decoupling capacitor C1 is 9.4 mm", v.Message);
            Assert.Equal(5.0, v.Location.X, 3);
        }

        [Fact]
        public void Non_supply_pin_on_a_rail_is_ignored()
        {
            // U1-1 sits on 3V3 but is an EN input in the schematic, not a supply pin.
            Assert.Empty(Run(capX: 10.0, PinType.Input, "EN"));
        }

        [Fact]
        public void Rotated_pads_use_edge_distance()
        {
            var a = new PadInfo { SizeX = 0.3, SizeY = 1.8, Rotation = 90 }; // 1.8 mm along X
            var b = new PadInfo { X = 2.0, SizeX = 0.5, SizeY = 0.5 };
            Assert.Equal(2.0 - 0.9 - 0.25, a.DistanceTo(b), 3);
        }
    }
}
