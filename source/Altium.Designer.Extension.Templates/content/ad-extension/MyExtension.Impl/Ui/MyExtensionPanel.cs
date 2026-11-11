using System;
using System.Windows.Forms;

namespace MyExtension.Impl.Ui
{
    /// <summary>
    /// Owns the content of the shim's "MyExtension" panel. The shim publishes an empty host control under
    /// <see cref="HostKey"/>; this class docks a <see cref="PanelContent"/> into it: when the panel is first shown
    /// (the shim calls Panel.Attach) and after every hot reload (HotEntry.OnLoad). The content is a control of the
    /// current generation, so HotEntry.OnUnload removes it: a shim control holding it would keep the old generation
    /// alive. State that must survive a reload belongs in AppDomain data as BCL types (strings, JSON), not in
    /// static fields.
    /// </summary>
    internal static class MyExtensionPanel
    {
        /// <summary>Must match MyExtensionPanelForm in the shim and the PanelInfo in MyExtension.ins.</summary>
        public const string PanelName = "MyExtensionPanel";

        private const string HostKey = "MyExtension.PanelHost";
        private const string InvokeKey = "MyExtension.Invoke";

        private static PanelContent _content;

        private static Control Host => AppDomain.CurrentDomain.GetData(HostKey) as Control;

        /// <summary>
        /// Runs a command the way the menus do: through the shim, so that in Debug the hot-reload host knows a
        /// command is running and never swaps the implementation under it. Throws when the command fails.
        /// </summary>
        public static string Invoke(string command, string parameters = "")
        {
            var invoke = AppDomain.CurrentDomain.GetData(InvokeKey) as Func<string, string, string>
                         ?? throw new UserException("The MyExtension panel is not available: redeploy the extension and restart Altium Designer.");
            return invoke(command, parameters);
        }

        /// <summary>
        /// Docks the content into the shim's host, unless it is already there. A no-op until AD has created the
        /// panel (at Release startup, OnLoad runs before that; the panel then attaches when it is first shown).
        /// </summary>
        public static void Attach()
        {
            var host = Host;
            if (host == null || _content != null && !_content.IsDisposed && _content.Parent == host)
                return;
            Detach();
            var stale = new Control[host.Controls.Count]; // e.g. the shim's load-error label
            host.Controls.CopyTo(stale, 0);
            host.Controls.Clear();
            foreach (var control in stale)
                control.Dispose();
            _content = new PanelContent { Dock = DockStyle.Fill };
            host.Controls.Add(_content);
        }

        /// <summary>
        /// Removes and disposes the content: the shim's host must not keep this generation alive. Release here, too,
        /// anything the content subscribed to outside itself (timers, Application.Idle, events of Altium or shim
        /// objects); disposing the control releases only its own child controls.
        /// </summary>
        public static void Detach()
        {
            if (_content == null)
                return;
            _content.Parent?.Controls.Remove(_content);
            _content.Dispose();
            _content = null;
        }

        /// <summary>Opens the panel and gives it the focus.</summary>
        public static void Show()
        {
            var gui = DXP.GlobalVars.GUIManager ?? throw new UserException("The Altium GUI manager is not available.");
            gui.SetPanelVisibleInCurrentForm(PanelName, true);
            gui.SetPanelActiveInCurrentForm(PanelName);
            Attach();
        }
    }
}
