# Altium Designer Extension Template

[![CI](https://github.com/AltiumDeveloper/ad-extension-template/actions/workflows/ci.yml/badge.svg)](https://github.com/AltiumDeveloper/ad-extension-template/actions/workflows/ci.yml)
[![Altium.Designer.Extension.Templates](https://img.shields.io/nuget/v/Altium.Designer.Extension.Templates?label=Altium.Designer.Extension.Templates)](https://www.nuget.org/packages/Altium.Designer.Extension.Templates)
[![Altium.Designer.HotReload](https://img.shields.io/nuget/v/Altium.Designer.HotReload?label=Altium.Designer.HotReload)](https://www.nuget.org/packages/Altium.Designer.HotReload)

Create Altium Designer extensions in C# with one command, and change their code while AD keeps
running. Edit, build, and the new code answers within a few seconds, with no reinstall, no restart and
no reopening of your project.

| Package | What it is |
|---|---|
| [`Altium.Designer.Extension.Templates`](https://www.nuget.org/packages/Altium.Designer.Extension.Templates) | `dotnet new ad-extension`: a complete extension solution |
| [`Altium.Designer.HotReload`](https://www.nuget.org/packages/Altium.Designer.HotReload) | the hot reload host the generated extensions use (source-only, Debug builds only) |

## How it works

```
Altium Designer
 └─ MyTools.dll         the shim: what AD loads. Tiny; changes only with the menus.
     └─ MyTools.Impl.dll    all your code.
                            Debug: loaded from its build output, swapped on every rebuild.
                            Release: a normal DLL next to the shim. No hot reload code at all.
```

In Debug, a current-user named pipe lets a terminal script (or an AI agent) reload, inspect and
run your commands inside AD.

## Prerequisites

* Altium Designer 24 or later.
* .NET SDK 8 or later; **.NET SDK 10** for Altium Designer 26.12 and later (`dotnet --list-sdks`).
* PowerShell (built into Windows), and any editor. You don't need Visual Studio or the Altium Developer wizard.
* A PCB project to try it on.

## 1. Install the template

```bash
dotnet new install Altium.Designer.Extension.Templates
```

## 2. Create the extension

```bash
dotnet new ad-extension -n MyTools --altium-system-dir "C:\Program Files\Altium\AD26\System"
```

```bash
cd MyTools
dotnet build
```

`--altium-system-dir` is the `System` folder of the Altium Designer installation to build against: the one with
`Altium.SDK.dll` (default `C:\Program Files\Altium\AD26\System`). Everything else follows from it:

* **Target framework.** The installation names the .NET version it hosts in `X2.runtimeconfig.json`, next to the
  `System` folder; the build targets exactly that (`net8.0-windows` for 26.1, `net10.0-windows` for 26.12). After an
  Altium Designer update that changes the .NET version, just rebuild.
* **Extensions folder.** `tools/deploy.ps1` finds the installation's data folder
  (`%ProgramData%\Altium\Altium Designer {GUID}\Extensions`) through the registry.

Both paths are in `Directory.Build.props`, where you can change them later (`AltiumSystemDir`, and
`AltiumExtensionsDir` to deploy to an Extensions folder of your choice).

Use a plain identifier for the name: letters and digits only, no dots or spaces. It becomes AD's server,
process and menu IDs.

| Option | Default | Adds |
|---|---|---|
| `--panel` | off | a dockable panel whose content is built by the implementation, so it hot-reloads too |
| `--outputer` | off | an OutJob output (Report category) whose content is written by a hot-reloadable command |
| `--tests` | on | an xUnit project for the implementation that runs without AD (`--tests false` to leave it out) |
| `--extensions-dir` | empty | deploy to this Extensions folder instead of the installation's own (sets `AltiumExtensionsDir`) |

## 3. Install once, restart AD once

```bash
powershell -ExecutionPolicy Bypass -File tools/deploy.ps1
```

This builds Debug and copies the shim into `Extensions\MyTools`. It also registers the extension in
`ExtensionsRegistry.xml`, keeping a backup; AD ignores unregistered folders. It works while AD is running.

**Restart AD.** You should see a **MyTools** menu between Reports and Window. It has **Hello** and,
in Debug builds, **Developer** with Reload Implementation and Hot Reload Status. Focus a PCB project
and click **Hello**. The message box shows the project name, its net count and `generation 1`.

## 4. The loop

AD starts an extension on its first command, so click a MyTools command once per AD session. Then:

```bash
powershell -ExecutionPolicy Bypass -File tools/ad.ps1 run Hello
```

```
Hello from MyBoard.PrjPcb: 184 nets (generation 1, origin Pipe).
```

Now change the text in `MyTools.Impl\Commands\HelloCommand.cs` and run:

```bash
powershell -ExecutionPolicy Bypass -File tools/dev.ps1 Hello
```

```
build: 2.2s
Hello again from MyBoard.PrjPcb: 184 nets (generation 2, origin Pipe).
```

The menu item shows the new text too. To rebuild on every save instead:

```bash
powershell -ExecutionPolicy Bypass -File tools/dev.ps1 -Watch
```

| Command | What it does |
|---|---|
| `tools/dev.ps1 [<Cmd> ["K=V\|K=V"]]` | build, reload, run (default `Hello`) |
| `tools/dev.ps1 -Watch` | rebuild on every save; AD swaps each build in |
| `tools/ad.ps1 run <Cmd> [K=V\|K=V]` | run a command inside AD |
| `tools/ad.ps1 list` | commands of the loaded build |
| `tools/ad.ps1 status [-gc]` | loaded generation, last error, unload diagnostics |
| `tools/ad.ps1 reload` | force a reload |

Exit codes: 0 ok, 1 command failed or build rejected, 2 usage error or several instances, 3 AD not
reachable. A build that fails to compile, or that AD rejects, never replaces the running one.

## 5. Add your own command

Add a file, say `MyTools.Impl\Commands\BoardSummaryCommand.cs` (any static method of the implementation works):

```csharp
using PCB; // GetCurrentPCBBoard and the other PCB object methods are extension methods in this namespace

namespace MyTools.Impl.Commands
{
    internal static class BoardSummaryCommand
    {
        [HotCommand("BoardSummary", "Count components and nets on the open board")]
        public static string BoardSummary(CommandContext ctx)
        {
            var board = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard()
                        ?? throw new UserException("Open a .PcbDoc first.");
            // iterate with board.BoardIterator_Create() ... board.BoardIterator_Destroy(ref iterator)
            return $"{board.GetState_FileName()}: ...";
        }
    }
}
```

Run `tools/dev.ps1 BoardSummary` and it runs right away, with no menu change. `tools/ad.ps1 list` shows it.
The complete command is in [`examples/BoardTools`](examples/BoardTools). For schematic checks, cross-probing from
the Messages panel and offline tests, see [`examples/DesignChecks`](examples/DesignChecks).

To put it on the menu, add a `PL` to `MyTools\Installation\MyTools.rcs`, outside the `#if DEBUG` blocks (Release
drops those), and a `Link` to it inside `Tree MNMyTools`, above that tree's `#if DEBUG` line:

```
PL PLMyTools:BoardSummary
    Command='MyTools:Run'
    Caption='&Board Summary'
    Image=''
    Description='Count components and nets on the open board'
    Params='Cmd=BoardSummary'
End

    Link      MNMyTools_BoardSummary  PLID='PLMyTools:BoardSummary'  End
```

IDs must be unique. To add the item to one of AD's own menus instead, use an `Insertion` like the template's three
(AD's own `.rcs` files in its `System` folder list the menu IDs). Then redeploy and restart AD: menus are the one
thing that needs a restart.

Rules for hot code: don't cache Altium objects across commands (query the workspace each time). Undo
static event subscriptions, timers, threads and open forms in `HotEntry.OnUnload`. Inside AD a relative path
resolves against AD's installation folder, and in Debug `Assembly.Location` of the implementation is empty: use
explicit paths. Notification handlers, menu items with their own enabled/visible state, and native callbacks
belong in the shim, through the hooks of `PluginServerModule` (`RegisterCommands`, `OnCreated`); the generated
README shows each.

## Optional: a panel

With `--panel`, the extension gets a dockable **MyTools** panel (**MyTools › Show Panel**, or AD's Panels
button). AD first opens it floating; dock it where you like and AD remembers the place. The shim only hosts an
empty control (`MyTools\Panel.cs` and the `PanelInfo` in `MyTools.ins`); the content is
`MyTools.Impl\Ui\PanelContent.cs`, which lists the extension's commands and runs them on a double-click. Replace it
with your own UI: it is rebuilt on every hot reload (`HotEntry.OnLoad`/`OnUnload`), so you can work on it without
restarting AD. The generated README lists the rules (run commands through the shim, release subscriptions in
`Detach`, keep state that must survive a reload outside the control). The panel's shim classes and `PanelInfo`
change only with a redeploy and a restart.

## Optional: an OutJob output

With `--outputer`, an OutJob offers **Add New Report Output › MyTools Report**. Link it to a container
(for example **Folder Structure**) and click **Generate content**: it writes `<project>.txt` with the project's
components and nets. The content comes from `MyTools.Impl\Commands\ReportCommand.cs`, so it hot-reloads too.
Edit it, run `tools/dev.ps1 Report` (which writes the focused project's report to `%TEMP%`), or generate the
OutJob again, all without a restart. The outputer in the shim (`MyTools\ReportOutput.cs`) and
`Installation\MyTools.out` change only with a redeploy and a restart.

The outputer derives from `Altium.Edp.Classes.OutputGenerator` in `Altium.SDK.dll`, the base class Altium
Designer's own .NET outputers use. It isn't covered by the SDK reference yet: treat `--outputer` as a preview.

## Tests

`dotnet test` runs `MyTools.Tests` without AD. Test logic that doesn't touch Altium objects: read what a
command needs from AD into plain C# records first (a model), then check the model. `examples/DesignChecks`
shows the pattern for an ERC and a DRC.

## 6. Release

```bash
powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Configuration Release
```

Then restart AD. Release installs `MyTools.dll` and `MyTools.Impl.dll`, and the shim calls your code
directly. There's no pipe, no file watcher, no dynamic loading and no Developer menu, and the
hot reload sources aren't even compiled. To get back to the loop, deploy Debug again and restart.

## Examples

Each example is a complete extension, generated with this template:

* [`examples/BoardTools`](examples/BoardTools): the smallest real command, board statistics to the Messages panel.
* [`examples/DesignChecks`](examples/DesignChecks): a decoupling ERC and DRC with cross-probing, and offline tests.

Set `AltiumSystemDir` in its `Directory.Build.props` if your installation is elsewhere, then follow steps 3–4
inside its folder.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Build error CS1705 (`System.Runtime, Version=10.0.0.0` …) | The project targets a .NET version other than the one the installation hosts, usually because `AltiumTargetFramework` is set by hand. Remove the override, or point `AltiumSystemDir` at the right installation. |
| `deploy.ps1`: `No Altium Designer installation is registered at …` | `AltiumSystemDir` is not the `System` folder of an installed AD. The message lists the installations found. |
| No menu, and the commands aren't in AD's search | The extension isn't in `Extensions\ExtensionsRegistry.xml`. Run `tools/deploy.ps1`, which registers it, and restart AD. |
| Commands are in search but there's no menu | `Updates = 'AdvSch'` is missing from `Installation\*.ins`. Keep the template's line. |
| `No hot reload instance found` right after a restart | Normal: AD starts the extension on its first command. Click any of its menu items once. If it persists, AD is running a Release shim: deploy Debug and restart. |
| `Several instances are running` | Two hot-reload extensions or AD processes. Your project's `tools/ad.ps1` already passes `-Name`; add `-Pid <n>` for several AD processes. |
| `ERROR: Build … rejected: …` | The build compiled, but `HotEntry` is missing or its signature changed. Fix it and build again. AD keeps running the previous build meanwhile. |
| `Pending: generation(s) 1 …` after `status -gc` | Expected: AD's WPF keeps the first load of new builds until AD restarts. Many pending generations point to an event, timer or form you didn't release in `OnUnload`. |
| AD fails to load the extension after you copy files by hand | Don't put a `runtimeconfig.json` in the extension folder, and don't build straight into it. Use `tools/deploy.ps1`, which mirrors the shim output and renames locked files aside. |

Logs: `%LOCALAPPDATA%\Altium.Designer.HotReload\<Name>.log` records every load, rejection and pipe request.
Full reference: [`source/Altium.Designer.HotReload/README.md`](source/Altium.Designer.HotReload/README.md).

## Security

The dev pipe is created only by **Debug** shims. It's restricted to the current Windows user, but
that user's processes can run any command of your extension inside AD, with AD's access to your
designs. **Never distribute a Debug build.** Ship Release (`deploy.ps1 -Configuration Release`).
You can check it: neither Release DLL contains `NamedPipeServerStream` or `AssemblyLoadContext`.
To report a vulnerability, see [SECURITY.md](SECURITY.md).

## Uninstall

* **An extension:** close AD, delete `Extensions\MyTools`, and remove its `<Item HRID="MyTools">`
  element from `Extensions\ExtensionsRegistry.xml`.
* **The template:** `dotnet new uninstall Altium.Designer.Extension.Templates`.

## Known limitations

* AD menu changes (`.rcs`/`.ins`) and shim changes need a redeploy and an AD restart. Code changes don't.
* WPF keeps the first load of each new build in memory until AD restarts (well under 1 MB per build).

## Repository layout

```
source/Altium.Designer.Extension.Templates/   the template package (content/ad-extension is the template itself)
source/Altium.Designer.HotReload/             the hot reload package: sources, tests, full reference
examples/                                     extensions generated with the template
.github/workflows/                            CI, and the NuGet releases
```

Building from source, running the tests and releasing are described in [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE), Copyright (c) 2026 Altium Limited.
