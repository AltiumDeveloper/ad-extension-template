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

An Altium Designer extension (AD 24+) with a hot-reloadable implementation in Debug builds:
edit C#, build, and the running AD picks up the new code within a few seconds, with no restart.

## Architecture

```
Altium Designer
 └─ BoardTools.dll           shim: the only DLL AD loads. Small and stable; changes need an AD restart.
     │  Debug:   Altium.Designer.HotReload (source package, compiled in): collectible AssemblyLoadContext,
     │           file watcher, dev pipe \\.\pipe\BoardTools.HotReload.<pid> (current user only)
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

Set `AltiumSystemDir` in `Directory.Build.props` rather than through an environment variable or `-p:`: `deploy.ps1`
and `dev.ps1` must build for the same installation (`dev.ps1` warns when they don't). Then:

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

Add a file to `BoardTools.Impl\Commands` (any static method of the implementation works):

```csharp
using EDP; // the DM_* methods of projects and documents are extension methods in EDP
using PCB; // the same for PCB objects (GetCurrentPCBBoard, ...)

namespace BoardTools.Impl.Commands
{
    internal static class GreetCommand
    {
        [HotCommand("Greet", "Says hello")]
        public static string Greet(CommandContext ctx) => $"Hello, {ctx.Get("Name", "world")}";
    }
}
```

Build, then `tools/ad.ps1 run Greet "Name=Ada"`. From a menu, the returned text is shown in a
message box; throw `UserException` for expected errors.

For a menu item, add to `Installation\BoardTools.rcs` a `PL` (outside the `#if DEBUG` blocks, or Release drops it)
and a `Link` inside `Tree MNBoardTools`, above its `#if DEBUG` line; IDs must be unique:

```
PL PLBoardTools:Greet
    Command='BoardTools:Run'
    Caption='&Greet'
    Image=''
    Description='Says hello'
    Params='Cmd=Greet|Name=Ada'
End

    Link      MNBoardTools_Greet  PLID='PLBoardTools:Greet'  End
```

To put an item into one of AD's own menus instead, add an `Insertion` like the template's (`TargetID` is the menu,
`RefID0` the item to insert before or after; AD's own `.rcs` files in its `System` folder list the IDs). Menu
changes need a redeploy and an AD restart.

Rules for hot code (Debug): don't cache Altium COM objects across commands, and release static event
subscriptions, timers, threads and open forms in `HotEntry.OnUnload`, otherwise the old generation
stays in memory (`tools/ad.ps1 status -gc` shows it). One or a few pending generations are expected:
AD's WPF keeps the first load of new builds until AD restarts (see the package README).

## Release

```bash
powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Configuration Release
```

installs `BoardTools.dll`, `BoardTools.Impl.dll` and the installation files (`Installation\*`) without the
Developer menu. The Release shim contains no hot-reload code. Restart AD to switch configuration, and deploy Debug
again to get back to the inner loop.

## Hot-reload package

Debug builds compile in [`Altium.Designer.HotReload`](https://www.nuget.org/packages/Altium.Designer.HotReload),
a source-only package from nuget.org; its README covers the contract, the pipe protocol and the limitations.
`tools/hotreload.ps1` is the package's pipe client.

## Layout

* `BoardTools\`: the shim. `PluginServerModule.cs`, `Commands.cs`, `Implementation.cs`, and `Installation\` (the
  files AD reads: `.ins`, `.rcs`).
* `BoardTools.Impl\`: the implementation. `Framework\` (HotEntry, CommandContext, CommandRegistry, AltiumSession) and
  `Commands\`.
* `tools\`: `deploy.ps1`, `dev.ps1`, `ad.ps1`, and `hotreload.ps1` (the pipe client from the package).
* `Directory.Build.props`: Altium paths and shared build settings.
