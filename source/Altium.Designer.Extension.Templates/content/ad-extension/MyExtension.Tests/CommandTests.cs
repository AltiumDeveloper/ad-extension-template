using System;
using System.Linq;
using MyExtension.Impl;
using Xunit;

namespace MyExtension.Tests
{
    public class CommandTests
    {
        [Fact]
        public void Parameters_are_parsed_altium_style()
        {
            var parameters = CommandContext.ParseParameters("File=C:\\out.txt| Verbose |a=1");

            Assert.Equal("C:\\out.txt", parameters["File"]);
            Assert.Equal(string.Empty, parameters["Verbose"]);
            Assert.Equal("1", parameters["A"]); // keys are case-insensitive
        }

        [Fact]
        public void Flags_accept_bare_true_and_1()
        {
            var ctx = new CommandContext("Hello", "A|B=true|C=1|D=0", null);

            Assert.True(ctx.Flag("A") && ctx.Flag("B") && ctx.Flag("C"));
            Assert.False(ctx.Flag("D") || ctx.Flag("Missing"));
        }

        [Fact]
        public void Hot_commands_are_discovered()
        {
            Assert.Contains("Hello", CommandRegistry.All.Select(c => c.Name));
            Assert.Equal(CommandRegistry.All.Count, CommandRegistry.All.Select(c => c.Name.ToUpperInvariant()).Distinct().Count());
        }

        [Fact]
        public void Unknown_commands_are_rejected()
        {
            var ex = Assert.Throws<ArgumentException>(() => HotEntry.Invoke("NoSuchCommand", "", null));
            Assert.Contains("Hello", ex.Message); // lists the available commands
        }
    }
}
