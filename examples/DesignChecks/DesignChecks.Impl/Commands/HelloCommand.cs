using System.IO;
using EDP;

namespace DesignChecks.Impl.Commands
{
    internal static class HelloCommand
    {
        [HotCommand("Hello", "Report the focused project's name and net count")]
        public static string Hello(CommandContext ctx)
        {
            // Re-query the workspace on every call; never cache Altium COM objects across commands.
            var project = AltiumSession.RequireCompiledProject();
            var name = Path.GetFileName(project.DM_ProjectFullPath());
            var nets = project.DM_DocumentFlattened()?.DM_NetCount() ?? 0;
            return $"Hello from {name}: {nets} nets (generation {ctx.Generation}, origin {ctx.Origin}).";
        }
    }
}
