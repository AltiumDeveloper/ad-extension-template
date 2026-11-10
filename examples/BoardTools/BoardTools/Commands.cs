using System;
using System.Linq;
using System.Windows.Forms;
using DXP;

/// <summary>
/// The AD processes this extension declares (Installation\BoardTools.ins). "Run" is generic on purpose:
/// features are [HotCommand] methods in BoardTools.Impl, reached through "BoardTools:Run" with
/// "Cmd=&lt;Name&gt;", so neither the menus nor this shim change when the implementation grows.
/// </summary>
public static class Commands
{
    private const string Caption = "BoardTools";
    private static string _startupError;
    private static bool _started;

    /// <summary>Once per AD session, on AD's UI thread: from InitializeCommands, or from an outputer that runs first.</summary>
    internal static void Start()
    {
        if (_started)
            return;
        _started = true;
        try { Implementation.Start(); }
        catch (Exception ex) { _startupError = ex.ToString(); }
    }

    /// <summary>Runs an implementation command without UI (outputers). Throws on failure.</summary>
    internal static string Invoke(string command, string parameters, object context)
    {
        Start();
        if (_startupError != null)
            throw new InvalidOperationException("BoardTools failed to start: " + _startupError);
        return Implementation.Invoke(command, parameters, context);
    }

    /// <summary>BoardTools:Run  Params: Cmd=&lt;HotCommand&gt;|Key=Value|...  Shows the command's result, if any.</summary>
    public static void Run(IServerDocumentView view, ref string parameters)
    {
        if (_startupError != null)
        {
            MessageBox.Show("BoardTools failed to start:\n\n" + _startupError, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var (command, rest) = ExtractCommand(parameters);
        try
        {
            var result = Implementation.Invoke(command, rest, view);
            if (!string.IsNullOrEmpty(result))
                MessageBox.Show(result, Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            // UserException is an expected failure: show its message only.
            var details = ex.GetType().Name == "UserException" ? ex.Message : ex.ToString();
            MessageBox.Show($"'{command}' failed:\n\n{details}", Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

#if DEBUG
    /// <summary>BoardTools:Reload - force a reload of the implementation (Debug only).</summary>
    public static void Reload(IServerDocumentView view, ref string parameters) => Show(Implementation.Reload);

    /// <summary>BoardTools:Status - hot reload diagnostics (Debug only).</summary>
    public static void Status(IServerDocumentView view, ref string parameters) => Show(Implementation.Status);

    private static void Show(Func<string> message)
    {
        try { MessageBox.Show(message(), Caption, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
#endif

    public static void GetState_Always(IServerDocumentView context, ref string parameters, ref bool enabled,
        ref bool isChecked, ref bool visible, ref string caption, ref string imageFile)
    {
        enabled = true;
        visible = true;
    }

    /// <summary>Splits "Cmd=X|A=1|B=2" into ("X", "A=1|B=2").</summary>
    private static (string Command, string Tail) ExtractCommand(string parameters)
    {
        var parts = (parameters ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isCmd = (Func<string, bool>)(p => p.StartsWith("Cmd=", StringComparison.OrdinalIgnoreCase));
        var command = parts.FirstOrDefault(isCmd)?.Substring(4).Trim() ?? string.Empty;
        return (command, string.Join("|", parts.Where(p => !isCmd(p))));
    }
}
