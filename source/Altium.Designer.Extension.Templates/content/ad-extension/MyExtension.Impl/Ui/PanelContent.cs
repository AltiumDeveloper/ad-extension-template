using System;
using System.Linq;
using System.Windows.Forms;
using DXP;
using EDP; // the workspace's DM_* methods (AltiumSession.Workspace...) are extension methods in EDP

namespace MyExtension.Impl.Ui
{
    /// <summary>
    /// The panel's content: the extension's commands, and the result of the last one run. Replace it with your own
    /// UI; it hot-reloads in Debug builds like any command. Use AD's theme colours (DxpThemeManager) so the panel
    /// reads well in both the light and the dark theme; they are read when the content is built, so a theme change
    /// shows after the next reload or restart.
    /// </summary>
    internal sealed class PanelContent : UserControl
    {
        private readonly ListBox _commands;
        private readonly TextBox _output;

        public PanelContent()
        {
            BackColor = DxpThemeManager.ColorPanelBody;
            ForeColor = DxpThemeManager.ColorPanelFont;

            // A table, not fixed pixel sizes: the header takes the height its text needs and the list and the
            // output share the rest, so the layout holds at any display scaling and panel size.
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0), Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var header = new Label
            {
                Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(4, 6, 4, 6), Margin = new Padding(0),
                Text = "Double-click a command to run it.",
            };
            _commands = new ListBox
            {
                Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, Margin = new Padding(0, 0, 0, 4),
                BackColor = DxpThemeManager.ColorEditBoxBack, ForeColor = DxpThemeManager.ColorDefaultFont,
            };
            _commands.DoubleClick += (_, _) => RunSelected();
            _commands.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RunSelected(); };

            _output = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                Margin = new Padding(0), BackColor = DxpThemeManager.ColorEditBoxBack, ForeColor = DxpThemeManager.ColorDefaultFont,
            };

            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(_commands, 0, 1);
            layout.Controls.Add(_output, 0, 2);
            Controls.Add(layout);

            // The panel's own commands open or fill this panel: leave them out.
            foreach (var command in CommandRegistry.All.Where(c => !c.Name.StartsWith("Panel.", StringComparison.OrdinalIgnoreCase)))
                _commands.Items.Add(new Item(command.Name, command.Description));
        }

        private void RunSelected()
        {
            if (_commands.SelectedItem is not Item item)
                return;
            string text;
            try
            {
                // Through the shim, like a menu command (not CommandRegistry.Execute): in Debug the hot-reload host
                // then defers any reload until the command returns.
                var result = MyExtensionPanel.Invoke(item.Name);
                text = string.IsNullOrEmpty(result) ? $"{item.Name}: done." : result.Replace("\n", Environment.NewLine);
            }
            catch (Exception ex)
            {
                // By name: in Debug the exception may come from another generation's UserException type.
                text = $"{item.Name} failed: " + (ex.GetType().Name == nameof(UserException) ? ex.Message : ex.ToString());
            }
            if (!IsDisposed) // a reload during the command replaced this content
                _output.Text = text;
        }

        private sealed record Item(string Name, string Description)
        {
            public override string ToString() => $"{Name}  -  {Description}";
        }
    }
}
