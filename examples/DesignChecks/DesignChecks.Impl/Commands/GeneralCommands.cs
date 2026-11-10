using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace DesignChecks.Impl.Commands
{
    internal static class GeneralCommands
    {
        [HotCommand("Ping", "Report which generation of the implementation is running")]
        public static string Ping(CommandContext ctx)
        {
            var assembly = typeof(GeneralCommands).Assembly;
            return $"pong: DesignChecks.Impl {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}, " +
                   $"generation {ctx.Generation}, origin {ctx.Origin}, load context '{AssemblyLoadContext.GetLoadContext(assembly)?.Name}'";
        }

        [HotCommand("Palette", "Show a window listing every command of the loaded implementation")]
        public static string Palette(CommandContext ctx)
        {
            if (!ctx.IsInteractive)
                return string.Join(Environment.NewLine, CommandRegistry.All.Select(c => $"{c.Name,-14} {c.Description}"));
            Ui.PaletteForm.ShowPalette(ctx.Generation);
            return null;
        }
    }
}
