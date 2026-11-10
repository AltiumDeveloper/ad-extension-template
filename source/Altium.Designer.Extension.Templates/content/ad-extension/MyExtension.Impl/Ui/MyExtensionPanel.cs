using System;
using System.Windows.Forms;

namespace MyExtension.Impl.Ui
{
    /// <summary>
    /// Owns the content of the shim's "MyExtension" panel. The shim publishes an empty host control under
    /// <see cref="HostKey"/>; this class docks a <see cref="PanelContent"/> into it. The content is a control of the
    /// current generation, so it is removed in HotEntry.OnUnload (a shim control holding it would keep the old
    /// generation alive) and docked again in HotEntry.OnLoad. State that must survive a reload belongs in
    /// AppDomain data as BCL types (strings, JSON), not in static fields.
    /// </summary>
    internal static class MyExtensionPanel
    {
        /// <summary>Must match MyExtensionPanelForm in the shim and the PanelInfo in MyExtension.ins.</summary>
        public const string PanelName = "MyExtensionPanel";

        private const string HostKey = "MyExtension.PanelHost";

        private static PanelContent _content;

        private static Control Host => AppDomain.CurrentDomain.GetData(HostKey) as Control;

        /// <summary>Docks fresh content into the shim's host (no-op when AD has not created the panel).</summary>
        public static void Attach()
        {
            var host = Host;
            if (host == null)
                return;
            Detach();
            host.Controls.Clear();
            _content = new PanelContent { Dock = DockStyle.Fill };
            host.Controls.Add(_content);
        }

        /// <summary>Removes and disposes the content: the shim's host must not keep this generation alive.</summary>
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
            if (_content == null)
                Attach();
        }
    }
}
