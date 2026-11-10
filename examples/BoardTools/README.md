# BoardTools

> **Example.** Generated with `dotnet new ad-extension -n BoardTools --tests false`, plus one command:
> `BoardTools.Impl\Commands\BoardSummaryCommand.cs` counts components, nets, tracks, vias, pads and arcs
> on the open board and posts the result to the Messages panel (`NoPanel`: terminal only). It is the
> smallest real command: a PCB board iterator, the Messages panel and a menu item in
> `BoardTools\Installation\BoardTools.rcs`. If your installation is not in `C:\Program Files\Altium\AD26`,
> set `AltiumSystemDir` in `Directory.Build.props`.
>
> ```bash
> powershell -ExecutionPolicy Bypass -File tools/ad.ps1 run BoardSummary NoPanel
> ```
> ```
> PCB.PcbDoc: components: 107, nets: 96, tracks: 2855, vias: 347, pads: 544, arcs: 39
> ```

An Altium Designer extension (AD 24+, .NET 8) with a hot-reloadable implementation in Debug builds:
edit C#, build, and the running AD picks up the new code in about 2 s, with no restart.

## Architecture

```
Altium Designer
 └─ BoardTools.dll           shim: the only DLL AD loads. Small and stable; changes need an AD restart.
     │  Debug:   Altium.Designer.HotReload (source package, compiled in): collectible AssemblyLoadContext,
     │           file watcher, dev pipe \.\pipe\BoardTools.HotReload.<pid> (current user only)
     │  Release: plain reference, no hot-reload code at all
     └─ BoardTools.Impl.dll  all real code: HotEntry → [HotCommand] methods
```

* Menus call one generic process, `BoardTools:Run` with `Params='Cmd=<Name>'`. A new feature is a new
  `[HotCommand]` method; the shim and `Installation\*` don't change.
* `BoardTools\Implementation.cs` is the only Debug/Release switch:
  * **Debug**: the shim loads `BoardTools.Impl\bin\Debug\...\BoardTools.Impl.dll` (path baked in
    at build time) and swaps in every rebuild. The **Developer** submenu (Reload, Status) and the
    dev pipe exist only in Debug.
  * **Release**: the shim references `BoardTools.Impl` and calls `HotEntry` directly. Both DLLs
    are deployed side by side. No watcher, no pipe, no dynamic loading.
* `HotEntry` uses BCL types only, so in Debug the implementation can change freely without
  rebuilding the shim.

## Setup

`Directory.Build.props` names the AD installation to build against: `AltiumSystemDir`, its `System` folder
(the one with `Altium.SDK.dll`). The rest follows from it:

* The target framework is the .NET version the installation hosts, read from `X2.runtimeconfig.json` next to
  `System` (`net8.0-windows` for 26.1, `net10.0-windows` for 26.12). After an AD update that changes it, rebuild
  and redeploy. `-p:AltiumTargetFramework=…` overrides it.
* `tools/deploy.ps1` deploys to that installation's Extensions folder
  (`%ProgramData%\Altium\Altium Designer {GUID}\Extensions`, found through the registry), unless
  `AltiumExtensionsDir` names one.

Then:

```bash
powershell -ExecutionPolicy Bypass -File tools/deploy.ps1
```

and restart AD **once**. The menu **BoardTools** (with **Hello**, and **Developer** in Debug builds)
appears between Reports and Window in the schematic, PCB and no-document menus. `deploy.ps1` works
while AD is running: locked files are renamed aside, and AD picks the new shim up on its next restart.
Re-run it only after changing `BoardTools\*` or `Installation\*`, or to switch configuration. Never
build straight into the extension folder.

AD loads only the extensions listed in `Extensions\ExtensionsRegistry.xml`; a folder alone is
silently ignored. The first `deploy.ps1` adds the entry the Altium Developer wizard would write
(it keeps a `.bak-<timestamp>` copy of the file). To uninstall, close AD, delete the
`Extensions\BoardTools` folder and remove the `<Item HRID="BoardTools">` element from that file.

Keep `Updates = 'AdvSch'` in `Installation\BoardTools.ins`. Without it, AD merges the `.rcs` only when
the extension server first starts, after the menu bar is built, so the menu never shows (the commands
do appear in AD's search). AD also starts the server **on first use**, so the dev pipe, and
`tools/ad.ps1`, work only after the extension's first command in an AD session.

## Inner loop (Debug)

```bash
powershell -ExecutionPolicy Bypass -File tools/dev.ps1
```

This builds the implementation, the running AD reloads it, and `Hello` runs; its result is printed
in the terminal.

| Command | What it does |
|---|---|
| `tools/dev.ps1 [<Cmd> ["K=V\|K=V"]]` | build, reload, run a command (default `Hello`) |
| `tools/dev.ps1 -Watch` | rebuild on every save; AD swaps each build in automatically |
| `tools/ad.ps1 run <Cmd> [K=V\|K=V]` | run a command inside AD |
| `tools/ad.ps1 list` | commands of the loaded generation |
| `tools/ad.ps1 status [-gc]` | loaded generation and unload diagnostics |
| `tools/ad.ps1 reload` | force a reload |
| `tools/ad.ps1 -Pid <n> …` | pick one AD instance when several run |

Exit codes: 0 ok, 1 command failed, 2 usage or several AD instances, 3 AD not reachable.

## Adding a command

```csharp
internal static class MyCommands
{
    [HotCommand("Greet", "Says hello")]
    public static string Greet(CommandContext ctx) => $"Hello, {ctx.Get("Name", "world")}";
}
```

Build, then `tools/ad.ps1 run Greet "Name=Ada"`. From a menu, the returned text is shown in a
message box; throw `UserException` for expected errors. For a menu item, add a `PL` with
`Command='BoardTools:Run' Params='Cmd=Greet'` to `Installation\BoardTools.rcs` and link it in the
`MNBoardTools` tree (needs a redeploy and an AD restart).

Rules for hot code (Debug): don't cache Altium COM objects across commands, and release static event
subscriptions, timers, threads and open forms in `HotEntry.OnUnload`, otherwise the old generation
stays in memory (`tools/ad.ps1 status -gc` shows it). One or a few pending generations are expected:
AD's WPF keeps the first load of new builds until AD restarts (see the package README).

## OutJob outputer (`--outputer`)

If the extension was created with `dotnet new ad-extension --outputer`, it also adds **BoardTools Report**
to the Report outputs of OutJob files:

* `Installation\BoardTools.out` declares the outputer: AD creates it with `InvokeOutputGenerator` of
  `CSharpPlugin.PluginFactory` in `BoardTools.dll`.
* `BoardTools\ReportOutput.cs` is the outputer. It is part of the shim and stays stable: it decides the file name
  (`<project>.txt` in the output folder) and hands the project to the implementation.
* `BoardTools.Impl\Commands\ReportCommand.cs` writes the content. Like any command it hot-reloads in Debug.
  `tools/dev.ps1 Report` writes the focused project's report to `%TEMP%` without running the OutJob.

In an OutJob: **Add New Report Output › BoardTools Report**, data source **[Project]**, link it to an output
container (e.g. **Folder Structure**), then **Generate content**. Changing the `.out` file, or the outputer in
the shim, needs a redeploy and an AD restart; changing the content doesn't.

The outputer derives from `Altium.Edp.Classes.OutputGenerator` in `Altium.SDK.dll`, the base class Altium Designer's
own .NET outputers use. It is not covered by the SDK reference yet: treat it as a preview.

## Panel (`--panel`)

If the extension was created with `dotnet new ad-extension --panel`, it has a dockable **BoardTools** panel
(**BoardTools › Show Panel**, the `Panel.Show` command, or AD's Panels button):

* `Installation\BoardTools.ins` declares it (`PanelInfo`, name `BoardToolsPanel`).
* `BoardTools\Panel.cs` is the panel's form and view in the shim. It stays an empty host control, published in
  AppDomain data, and is registered in `PluginServerModule.RegisterViews`.
* `BoardTools.Impl\Ui\BoardToolsPanel.cs` docks the content into that host: in `HotEntry.OnLoad` (every hot reload
  in Debug, startup in Release) and when the panel is first shown (`Panel.Attach`). `HotEntry.OnUnload` removes it,
  so the old generation can unload.
* `BoardTools.Impl\Ui\PanelContent.cs` is the content: the extension's commands, run on a double-click. Replace it
  with your own UI. It is rebuilt on every reload, so keep state that must survive one outside the control, as BCL
  types (for example JSON in AppDomain data, never your own types). Use `DxpThemeManager` colours so it reads
  well in AD's light and dark themes.

Changing `PanelInfo` or `Panel.cs` needs a redeploy and an AD restart; changing the content doesn't.

## Tests

`dotnet test` runs `BoardTools.Tests` (if the extension was created with tests) without AD. The Altium SDK
assemblies are referenced for compilation only, and no Altium object exists outside AD: test logic that
doesn't touch them. A good pattern is to read what a command needs from AD into plain C# records (a model), and
check the model in tests.

## Release

```bash
powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Configuration Release
```

installs `BoardTools.dll`, `BoardTools.Impl.dll` and the `.ins`/`.rcs` without the Developer
menu. The Release shim contains no hot-reload code. Restart AD to switch configuration, and
deploy Debug again to get back to the inner loop.

## Hot-reload package

Debug builds compile in [`Altium.Designer.HotReload`](https://www.nuget.org/packages/Altium.Designer.HotReload),
a source-only package from nuget.org; its README covers the contract, the pipe protocol and the limitations.
`tools/hotreload.ps1` is the package's pipe client.

## Layout

```
BoardTools\                    shim: PluginServerModule.cs, Commands.cs, Implementation.cs, Installation\*.ins/.rcs
BoardTools.Impl\               implementation: Framework\ (HotEntry, CommandContext, CommandRegistry, AltiumSession), Commands\
BoardTools.Tests\              offline tests
tools\                          deploy.ps1, dev.ps1, ad.ps1, hotreload.ps1 (pipe client from the package)
Directory.Build.props           Altium paths and shared build settings
```
