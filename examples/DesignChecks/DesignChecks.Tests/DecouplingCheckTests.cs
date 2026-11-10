using System.IO;
using System.Linq;
using DesignChecks.Impl;
using DesignChecks.Impl.Erc;
using Xunit;

namespace DesignChecks.Tests
{
    public class DecouplingCheckTests
    {
        private static Violation[] Run(ModelBuilder model) => DecouplingCheck.Run(model.Build(), new CheckConfig()).ToArray();

        private static ModelBuilder Ic(string designator = "U1", string supplyPin = "VDD", PinType type = PinType.Power) =>
            new ModelBuilder().Part(designator, ("1", supplyPin, type, "3V3"), ("2", "GND", PinType.Power, "GND"));

        [Fact]
        public void Ic_supply_without_capacitor_is_an_error()
        {
            var v = Assert.Single(Run(Ic().Two("R1", "3V3", "SIG")));
            Assert.Equal((DecouplingCheck.Missing, Severity.Error, "3V3"), (v.RuleId, v.Severity, v.Target.Name));
        }

        [Fact]
        public void Capacitor_to_ground_satisfies_decoupling()
        {
            Assert.Empty(Run(Ic().Two("C1", "3V3", "GND")));
        }

        [Fact]
        public void Fewer_capacitors_than_ics_is_a_warning()
        {
            var model = Ic().Part("U2", ("1", "VCC", PinType.Passive, "3V3"), ("2", "GND", PinType.Power, "GND")) // supply by pin name
                .Two("C1", "3V3", "GND");

            var v = Assert.Single(Run(model));
            Assert.Equal(DecouplingCheck.Low, v.RuleId);
            Assert.Contains("(U1, U2)", v.Message);
        }

        [Fact]
        public void Switching_regulator_node_through_inductor_is_exempt()
        {
            // MCU internal DC-DC: VLO pin -> L1 -> VCL, decoupled on the far side of the inductor.
            var model = new ModelBuilder()
                .Part("U1", ("1", "VLO", PinType.Power, "VLO"), ("2", "VCL", PinType.Power, "VCL"), ("3", "VSS", PinType.Power, "0V"))
                .Two("L1", "VLO", "VCL")
                .Two("C1", "VCL", "0V");

            Assert.Empty(Run(model));
        }

        [Fact]
        public void No_erc_net_is_skipped()
        {
            Assert.Empty(Run(Ic().Two("R1", "3V3", "X").NoErc("3V3")));
        }

        [Fact]
        public void Snapshot_round_trip_gives_the_same_findings()
        {
            // tools/ad.ps1 run Snapshot saves the focused project like this: check your own designs offline.
            var model = Ic().Part("U2", ("1", "VCC", PinType.Passive, "3V3"), ("2", "GND", PinType.Power, "GND")).Two("C1", "3V3", "GND").Build();
            var path = Snapshots.Save(model, Path.Combine(Path.GetTempPath(), "DesignChecks.Tests", Path.GetRandomFileName() + ".json"), null);
            try
            {
                var loaded = Snapshots.Load<DesignModel>(path).Link();

                var v = Assert.Single(DecouplingCheck.Run(loaded, new CheckConfig()));
                Assert.Equal((DecouplingCheck.Low, "3V3"), (v.RuleId, v.Target.Name));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
