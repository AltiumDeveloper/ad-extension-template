using MyExtension.Impl.Ui;

namespace MyExtension.Impl.Commands
{
    internal static class PanelCommands
    {
        [HotCommand("Panel.Show", "Open the MyExtension panel")]
        public static string Show(CommandContext ctx)
        {
            MyExtensionPanel.Show();
            return ctx.IsInteractive ? null : "MyExtension panel shown.";
        }

        /// <summary>Called by the shim's panel the first time it is shown, possibly before any other command ran.</summary>
        [HotCommand("Panel.Attach", "Fill the MyExtension panel (the panel calls this itself)")]
        public static string Attach(CommandContext ctx)
        {
            MyExtensionPanel.Attach();
            return null;
        }
    }
}
