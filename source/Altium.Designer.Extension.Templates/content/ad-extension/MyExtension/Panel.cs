using System;
using System.Windows.Forms;
using DXP;

namespace CSharpPlugin
{
    public partial class PluginServerModule
    {
        private MyExtensionPanelView _panel;

        // The global "MyExtension" panel (PanelInfo 'MyExtensionPanel' in Installation\MyExtension.ins): created
        // once, then registered with this module AND the client. Without the client registration AD never opens it.
        partial void RegisterViews(IClient client)
        {
            _panel = new MyExtensionPanelView(new MyExtensionPanelForm());
            AddServerView(_panel);
            client.AddServerView(_panel);
        }
    }
}

/// <summary>
/// The panel's form. Part of the shim, so it stays an empty host: the implementation builds the content and docks
/// it into <see cref="Host"/>, which it finds through AppDomain data (BCL types only, no shim types). The content
/// therefore hot-reloads like any command; only this file and the PanelInfo need a redeploy and an AD restart.
/// </summary>
internal sealed class MyExtensionPanelForm : ServerPanelForm
{
    public const string PanelName = "MyExtensionPanel"; // = PanelInfo Name in MyExtension.ins
    public const string Caption = "MyExtension";

    /// <summary>AppDomain data key under which the host control is published (= HostKey in the implementation).</summary>
    public const string HostKey = "MyExtension.PanelHost";

    /// <summary>
    /// AppDomain data key of the command invoker (= InvokeKey in the implementation): Func&lt;command, parameters, result&gt;.
    /// The panel runs commands through it, so they take the same route as menu commands: in Debug through the
    /// hot-reload host, which never swaps the implementation while one of its commands runs.
    /// </summary>
    public const string InvokeKey = "MyExtension.Invoke";

    private const string LoadErrorName = "MyExtensionPanelLoadError";

    public MyExtensionPanelForm()
    {
        Text = Caption;
        BackColor = DxpThemeManager.ColorPanelBody;
        Host = new Panel { Dock = DockStyle.Fill, Name = "MyExtensionPanelHost", BackColor = DxpThemeManager.ColorPanelBody };
        Controls.Add(Host);
        AppDomain.CurrentDomain.SetData(HostKey, Host);
        AppDomain.CurrentDomain.SetData(InvokeKey, (Func<string, string, string>)((command, parameters) => Commands.Invoke(command, parameters, null)));

        // The panel may be shown before any command ran: fill it the first time it becomes visible.
        VisibleChanged += (_, _) => AttachContent();
    }

    public Panel Host { get; }

    private void AttachContent()
    {
        if (!Visible || Host.Controls.Count > 0 && Host.Controls[0].Name != LoadErrorName)
            return;
        try
        {
            Commands.Invoke("Panel.Attach", "", null);
        }
        catch (Exception ex)
        {
            Host.Controls.Clear();
            Host.Controls.Add(new Label
            {
                Name = LoadErrorName, Dock = DockStyle.Fill, ForeColor = DxpThemeManager.ColorPanelFont,
                Text = Caption + " failed to load:\n\n" + ex.Message,
            });
        }
    }
}

/// <summary>The panel's view: registered with AD under the PanelInfo name.</summary>
internal sealed class MyExtensionPanelView : ServerPanelView
{
    public MyExtensionPanelView(MyExtensionPanelForm form) : base(form, MyExtensionPanelForm.PanelName, MyExtensionPanelForm.Caption)
    {
    }
}
