using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DesignChecks.Impl.Ui
{
    /// <summary>
    /// "DesignChecks > All Commands...": lists every hot command, double-click (or Enter) runs it. Reaches
    /// new commands without a menu edit. Modal, and closed on unload, so it never pins a generation.
    /// </summary>
    internal sealed class PaletteForm : Form
    {
        private static PaletteForm _open;

        private readonly ListBox _commands = new() { Dock = DockStyle.Top, Height = 200, Font = new Font("Consolas", 9f) };
        private readonly TextBox _output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };

        private PaletteForm(int generation)
        {
            Text = $"DesignChecks commands (generation {generation})";
            Size = new Size(820, 560);
            StartPosition = FormStartPosition.CenterScreen;
            _commands.Items.AddRange(CommandRegistry.All.Where(c => c.Name != "Palette").Select(c => $"{c.Name,-14} {c.Description}").ToArray<object>());
            _commands.DoubleClick += (_, _) => RunSelected();
            _commands.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RunSelected(); };
            Controls.Add(_output);
            Controls.Add(_commands);
        }

        public static void ShowPalette(int generation)
        {
            using var form = new PaletteForm(generation);
            _open = form;
            try { form.ShowDialog(); }
            finally { _open = null; }
        }

        public static void CloseIfOpen() => _open?.Close();

        private void RunSelected()
        {
            if (_commands.SelectedItem is not string item)
                return;
            var lines = new List<string>();
            var env = new Dictionary<string, object> { ["Origin"] = "UI", ["Log"] = (Action<string>)lines.Add };
            try
            {
                Cursor = Cursors.WaitCursor;
                lines.Add(CommandRegistry.Execute(new CommandContext(item.Split(' ')[0], "", env)));
            }
            catch (Exception ex)
            {
                lines.Add(ex is UserException ? ex.Message : "ERROR: " + ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            _output.Text = string.Join(Environment.NewLine, lines.Where(l => l != null)).ReplaceLineEndings();
        }
    }
}
