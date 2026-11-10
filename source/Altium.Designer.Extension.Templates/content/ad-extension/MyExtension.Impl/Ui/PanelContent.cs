using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using DXP;

namespace MyExtension.Impl.Ui
{
    /// <summary>
    /// The panel's content: the extension's commands, and the result of the last one run. Replace it with your own
    /// UI; it hot-reloads in Debug builds like any command. Use AD's theme colours (DxpThemeManager) so the panel
    /// reads well in both the light and the dark theme.
    /// </summary>
    internal sealed class PanelContent : UserControl
    {
        private readonly ListBox _commands;
        private readonly TextBox _output;

        public PanelContent()
        {
            BackColor = DxpThemeManager.ColorPanelBody;
            ForeColor = DxpThemeManager.ColorPanelFont;

            var header = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 24, Padding = new Padding(4, 4, 4, 0),
                Text = "Double-click a command to run it.",
            };
            _commands = new ListBox
            {
                Dock = DockStyle.Top, Height = 160, IntegralHeight = false, BorderStyle = BorderStyle.None,
                BackColor = DxpThemeManager.ColorEditBoxBack, ForeColor = DxpThemeManager.ColorDefaultFont,
            };
            _commands.DoubleClick += (_, _) => RunSelected();
            _commands.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RunSelected(); };

            _output = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = DxpThemeManager.ColorEditBoxBack, ForeColor = DxpThemeManager.ColorDefaultFont,
            };

            Controls.Add(_output);
            Controls.Add(new Splitter { Dock = DockStyle.Top, Height = 4 });
            Controls.Add(_commands);
            Controls.Add(header);

            // The panel's own commands open or fill this panel: leave them out.
            foreach (var command in CommandRegistry.All.Where(c => !c.Name.StartsWith("Panel.", StringComparison.OrdinalIgnoreCase)))
                _commands.Items.Add(new Item(command));
        }

        private void RunSelected()
        {
            if (_commands.SelectedItem is not Item item)
                return;
            try
            {
                var env = new Dictionary<string, object> { ["Origin"] = "Panel" };
                var result = CommandRegistry.Execute(new CommandContext(item.Command.Name, string.Empty, env));
                _output.Text = string.IsNullOrEmpty(result) ? $"{item.Command.Name}: done." : result.Replace("\n", Environment.NewLine);
            }
            catch (Exception ex)
            {
                _output.Text = $"{item.Command.Name} failed: " + (ex is UserException ? ex.Message : ex.ToString());
            }
        }

        private sealed record Item(CommandInfo Command)
        {
            public override string ToString() => $"{Command.Name}  -  {Command.Description}";
        }
    }
}
