using System.Collections.Generic;
using System.Linq;

namespace MyExtension.Impl
{
    /// <summary>
    /// Entry point the shim binds to: by name through the hot-reload host in Debug, as a normal call in Release.
    /// BCL-only signatures. Do not rename or change them; everything behind them can change freely.
    /// </summary>
    public static class HotEntry
    {
        public static string Invoke(string command, string parameters, IDictionary<string, object> env) =>
            CommandRegistry.Execute(new CommandContext(command, parameters, env));

        public static string[] GetCommands() =>
            CommandRegistry.All.Select(c => c.Name + "\t" + c.Description).ToArray();

        /// <summary>
        /// After this generation loads (env "Origin" = "Host"): in Debug on its first command and on every hot reload,
        /// in Release once, when the extension starts. Rebuild state that lives in the implementation, such as UI the
        /// shim hosts. The shim's panels may not exist yet at that point: attach to them when they are shown, too.
        /// </summary>
        public static void OnLoad(IDictionary<string, object> env)
        {
#if (Panel)
            Ui.MyExtensionPanel.Attach();
#endif
        }

        /// <summary>
        /// Debug only: called before this generation is unloaded. Release anything that would keep it
        /// alive: static event subscriptions on host/BCL objects, timers, threads, open forms, and
        /// controls of this generation inside the shim's (such as the panel content).
        /// </summary>
        public static void OnUnload()
        {
#if (Panel)
            Ui.MyExtensionPanel.Detach();
#endif
        }
    }
}
