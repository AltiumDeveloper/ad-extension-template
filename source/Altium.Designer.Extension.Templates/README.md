# Altium Designer extension templates

`dotnet new` template for an Altium Designer extension (AD 24+, .NET 8 or .NET 10 as the installation hosts):
a stable shim that AD loads, plus an implementation that hot-reloads in Debug builds.
It replaces the net48 project that the Altium Developer extension generates.

The full guide is the [repository README](https://github.com/AltiumDeveloper/ad-extension-template#readme).

## Install and use

```bash
dotnet new install Altium.Designer.Extension.Templates
```

```bash
dotnet new ad-extension -n AcmeTools --altium-system-dir "C:\Program Files\Altium\AD26\System"
```

| Option | Default | Meaning |
|---|---|---|
| `-n`, `--name` | folder name | Extension and AD server name. Replaces `MyExtension` everywhere: projects, namespaces, `.ins` `EditorName`/`EditorExePath`, the `AcmeTools:Run` process, menu IDs, the pipe name. Use a plain identifier (letters and digits, no dots or spaces): it becomes AD process and menu IDs. |
| `--altium-system-dir` | `C:\Program Files\Altium\AD26\System` | `System` folder of the AD installation to build against (has `Altium.SDK.dll`). The target framework comes from the installation's `X2.runtimeconfig.json`. |
| `--extensions-dir` | empty | Extensions folder to deploy to. Empty: `tools/deploy.ps1` finds the one of the installation in `--altium-system-dir`. |
| `--panel` | off | also add a dockable panel, **AcmeTools**, whose content is built by the hot-reloadable implementation |
| `--outputer` | off | also add an OutJob outputer, **AcmeTools Report** (Report category), whose content is written by the hot-reloadable `Report` command |
| `--tests` | on | add `AcmeTools.Tests`, an xUnit project that runs without AD |

The AD paths end up in the generated `Directory.Build.props`, where they can be changed later.

## What you get

```
AcmeTools.sln
AcmeTools\                      shim: PluginServerModule.cs, Commands.cs, Implementation.cs, Installation\AcmeTools.ins/.rcs
AcmeTools.Impl\                 implementation: Framework\ (HotEntry, CommandContext, CommandRegistry, AltiumSession, UserException)
                                and one sample command, Commands\HelloCommand.cs
AcmeTools.Tests\                offline tests (unless --tests false)
tools\                          deploy.ps1, dev.ps1 (inner loop), ad.ps1 + hotreload.ps1 (dev pipe client)
Directory.Build.props, .gitignore, README.md
```

The menu **AcmeTools** (schematic, PCB and no-document menus) has **Hello** and, in Debug builds
only, a **Developer** submenu with Reload and Status. The generated README explains the dev loop.

With `--panel`, the shim also has `Panel.cs` (the panel's form and view, registered in `PluginServerModule`)
and a `PanelInfo` in `AcmeTools.ins`; the implementation has `Ui\` (the content) and `Commands\PanelCommands.cs`
(`Panel.Show`, and `Panel.Attach`, which the panel calls itself).

With `--outputer`, the shim also has `ReportOutput.cs` (the `OutputGenerator` and the plugin factory's
`InvokeOutputGenerator`) and `Installation\AcmeTools.out`, and the implementation has
`Commands\ReportCommand.cs`.

**Debug vs Release.** Debug builds compile in the `Altium.Designer.HotReload` host and load the
implementation from its build output into a collectible AssemblyLoadContext, with a file watcher and
a dev pipe. Release builds reference the implementation project and call `HotEntry` directly: no load
context, watcher or pipe code is in the Release output, and the implementation DLL is deployed next to
the shim. The split is the shim's `Implementation.cs` (one `#if DEBUG`), the package's `HotModule="true"`
reference, and `#if DEBUG` blocks in the `.ins`/`.rcs`, which the shim build strips in Release.

## Working on the template

* The template engine evaluates `#if` in `.cs` files at generation time. The shim's `.cs` files that use
  `#if DEBUG` are wrapped in `//-:cnd:noEmit` / `//+:cnd:noEmit` so it reaches the generated code. The markers
  themselves are not emitted.
* Template options in `.ins` and `.rcs` files are `//#if (Panel)` … `//#endif` lines, and `#if (Tests)` in the
  `.sln` (see `SpecialCustomOperations` in `template.json`). The `#if DEBUG` lines of `.ins`/`.rcs` are not template
  conditionals: the shim build handles them.
* Symbol names must not match an MSBuild property used in a `Condition` (the engine would evaluate
  it). That's why the path symbols are `SystemDir` and `ExtensionsDir`, not `AltiumSystemDir`.
* `content/ad-extension` builds as-is (it is a valid solution named `MyExtension`, with every option on), which is
  the quickest way to work on the template. Delete its `bin`/`obj` folders afterwards; they are excluded
  from generation and packing anyway.
* `content/ad-extension/tools/hotreload.ps1` is a copy of `../Altium.Designer.HotReload/tools/hotreload.ps1`;
  CI fails when they differ.
* The generated shim references `Altium.Designer.HotReload` by version (`MyExtension/MyExtension.csproj`): bump it
  when a new HotReload version is released, and release HotReload first.

To try a local change:

```bash
dotnet pack -c Release -o bin
dotnet new install bin/Altium.Designer.Extension.Templates.1.0.0.nupkg --force
```
