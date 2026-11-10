//-:cnd:noEmit
using System.Collections.Generic;

/// <summary>
/// The shim's only route to MyExtension.Impl, and the only place that knows about Debug vs Release.
/// Debug:   Altium.Designer.HotReload loads it from its build output and swaps it on every rebuild.
/// Release: a plain static call through the ProjectReference; no hot reload code is compiled in.
/// </summary>
internal static class Implementation
{
#if DEBUG
    private static Altium.Designer.HotReload.HotReloadHost _host;

    /// <summary>Call once, on AD's UI thread (InitializeCommands).</summary>
    public static void Start() =>
        _host = Altium.Designer.HotReload.HotReloadHost.Start("MyExtension", Altium.Designer.HotReload.HotReloadBuild.ModulePath);

    public static string Invoke(string command, string parameters, object view) => _host.Invoke(command, parameters, view);

    public static string Reload() => _host.Reload() + "\n\n" + _host.Status(collect: false);

    public static string Status() => _host.Status(collect: true);
#else
    /// <summary>Call once, on AD's UI thread (InitializeCommands). Debug builds call OnLoad on every reload instead.</summary>
    public static void Start() =>
        MyExtension.Impl.HotEntry.OnLoad(new Dictionary<string, object> { ["Origin"] = "Host" });

    public static string Invoke(string command, string parameters, object view) =>
        MyExtension.Impl.HotEntry.Invoke(command, parameters, new Dictionary<string, object> { ["Origin"] = "UI", ["Context"] = view });
#endif
}
//+:cnd:noEmit
