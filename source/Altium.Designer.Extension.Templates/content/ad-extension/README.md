# MyExtension

An Altium Designer extension (AD 24+) with a hot-reloadable implementation in Debug builds:
edit C#, build, and the running AD picks up the new code within a few seconds, with no restart.

## Architecture

```
Altium Designer
 └─ MyExtension.dll           shim: the only DLL AD loads. Small and stable; changes need an AD restart.
     │  Debug:   Altium.Designer.HotReload (source package, compiled in): collectible AssemblyLoadContext,
     │           file watcher, dev pipe \\.\pipe\MyExtension.HotReload.<pid> (current user only)
     │  Release: plain reference, no hot-reload code at all
     └─ MyExtension.Impl.dll  all real code: HotEntry → [HotCommand] methods
```

* Menus call one generic process, `MyExtension:Run` with `Params='Cmd=<Name>'`. A new feature is a new
  `[HotCommand]` method; the shim and `Installation\*` don't change.
* `MyExtension\Implementation.cs` is the only Debug/Release switch:
  * **Debug**: the shim loads `MyExtension.Impl\bin\Debug\...\MyExtension.Impl.dll` (path baked in
    at build time) and swaps in every rebuild. The **Developer** submenu (Reload, Status) and the
    dev pipe exist only in Debug.
  * **Release**: the shim references `MyExtension.Impl` and calls `HotEntry` directly. Both DLLs
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

and restart AD **once**. The menu **MyExtension** (with **Hello**, and **Developer** in Debug builds)
appears between Reports and Window in the schematic, PCB and no-document menus. `deploy.ps1` works
while AD is running: locked files are renamed aside, and AD picks the new shim up on its next restart.
Re-run it only after changing `MyExtension\*` or `Installation\*`, or to switch configuration. Never
build straight into the extension folder.

AD loads only the extensions listed in `Extensions\ExtensionsRegistry.xml`; a folder alone is
silently ignored. The first `deploy.ps1` adds the entry the Altium Developer wizard would write
(it keeps a `.bak-<timestamp>` copy of the file). To uninstall, close AD, delete the
`Extensions\MyExtension` folder and remove the `<Item HRID="MyExtension">` element from that file.

Keep `Updates = 'AdvSch'` in `Installation\MyExtension.ins`. Without it, AD merges the `.rcs` only when
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

Add a file to `MyExtension.Impl\Commands` (any static method of the implementation works):

```csharp
using EDP; // the DM_* methods of projects and documents are extension methods in EDP
using PCB; // the same for PCB objects (GetCurrentPCBBoard, ...)

namespace MyExtension.Impl.Commands
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

For a menu item, add to `Installation\MyExtension.rcs` a `PL` (outside the `#if DEBUG` blocks, or Release drops it)
and a `Link` inside `Tree MNMyExtension`, above its `#if DEBUG` line; IDs must be unique:

```
PL PLMyExtension:Greet
    Command='MyExtension:Run'
    Caption='&Greet'
    Image=''
    Description='Says hello'
    Params='Cmd=Greet|Name=Ada'
End

    Link      MNMyExtension_Greet  PLID='PLMyExtension:Greet'  End
```

To put an item into one of AD's own menus instead, add an `Insertion` like the template's (`TargetID` is the menu,
`RefID0` the item to insert before or after; AD's own `.rcs` files in its `System` folder list the IDs). Menu
changes need a redeploy and an AD restart.

Rules for hot code (Debug): don't cache Altium COM objects across commands, and release static event
subscriptions, timers, threads and open forms in `HotEntry.OnUnload`, otherwise the old generation
stays in memory (`tools/ad.ps1 status -gc` shows it). One or a few pending generations are expected:
AD's WPF keeps the first load of new builds until AD restarts (see the package README).

Two more traps, both of which work in Release and fail only under hot reload or only inside AD:

* AD's working directory is its installation folder, so a relative path in your code resolves under
  `C:\Program Files\Altium\...` and writing to it fails with `UnauthorizedAccessException`. Build output paths from
  an explicit base (a parameter, the project's folder, `Path.GetTempPath()`).
* In Debug the implementation is loaded from bytes, so `Assembly.Location` of `MyExtension.Impl` is empty. Don't
  look for data files "next to the DLL": embed them as resources (`EmbeddedResource`, read with
  `GetManifestResourceStream`), or take their path from a parameter.

## Code in the shim

Some things must live in the shim (`MyExtension\`), which is never unloaded: whatever AD or native code calls back
into on its own. Add them as another part of `partial class PluginServerModule` (a new file in `MyExtension\`), and
keep them small: like menus, they change only with a redeploy and an AD restart. `PluginServerModule` has three
hooks, each compiled away when nothing implements it:

| Hook | Called | Use it for |
|---|---|---|
| `RegisterCommands(CommandLauncher)` | at the end of `InitializeCommands`, after `Run` | processes of your own |
| `RegisterViews(IClient)` | in the constructor | panels (taken by `Panel.cs` with `--panel`) |
| `OnCreated(IClient)` | in the constructor, after `RegisterViews` | any other one-time setup, such as a notification handler |

**A menu item with its own state.** `Run` is always enabled and visible. For an item that should be disabled or
hidden in some contexts (a context-menu item shown only over the right object, say), register a process with its
own GetState procedure, declare it in `Installation\MyExtension.ins` (`Command Name = 'RunOnPart' ... End`) and point
the `PL` at it (`Command='MyExtension:RunOnPart'`, `Params='Cmd=<HotCommand>'`):

```csharp
public partial class PluginServerModule
{
    partial void RegisterCommands(CommandLauncher launcher) =>
        launcher.RegisterCommand("RunOnPart", Commands.Run, GetState_RunOnPart);

    // AD calls it whenever it builds a menu holding the item: keep it fast, and let nothing escape.
    private static void GetState_RunOnPart(IServerDocumentView view, ref string parameters, ref bool enabled,
        ref bool isChecked, ref bool visible, ref string caption, ref string imageFile)
    {
        try { enabled = visible = /* your test */ true; }
        catch (Exception) { enabled = visible = false; }
    }
}
```

**Notifications.** `ServerModule.ReceiveNotification` isn't virtual, so you can't override it. Create the SDK's
own handler class with a callback, keep it in a field, and forward to the implementation:

```csharp
public partial class PluginServerModule
{
    private NotificationHandler _notifications;

    partial void OnCreated(IClient client)
    {
        _notifications = new NotificationHandler(OnNotification); // the constructor registers it with AD
    }

    private static void OnNotification(INotification notification)
    {
        try
        {
            if (string.Equals(notification?.GetNotificationType(), NotificationType.DocumentNotification,
                    StringComparison.OrdinalIgnoreCase))
                Commands.Invoke("OnDocumentNotification", "Code=" + notification.GetNotificationCode(), notification);
        }
        catch (Exception) { /* never let an exception reach AD's notification loop */ }
    }
}
```

Use a `DXP.NotificationHandler` with a callback, as above, rather than a handler class of your own. Its constructor
registers it: do not call `RegisterNotificationHandler` again, or every notification arrives twice. Keep it in a field
while it should receive notifications; `IClient.UnregisterNotificationHandler` stops it. The callback receives every
notification of the whole of AD, so filter first: the kinds are the `DXP.NotificationType` constants, and each kind's
codes are in the class nested in `DXP.NotificationCode` (`DocumentNotificationCode` for documents).

**Timers and native callbacks.** A `System.Windows.Forms.Timer` created in the shim ticks on AD's UI thread and may
call the implementation through `Commands.Invoke`. A callback handed to native code (a Win32 hook, a callback into a
native DLL) belongs in the shim too, in a field for as long as native code holds it: a delegate of the implementation
points into code that a hot reload unloads, and a call into it can take AD down with no exception to catch. If one must live in the
implementation, remove it in `HotEntry.OnUnload`.
<!--#if (Outputer) -->

## OutJob outputer

The extension adds **MyExtension Report** to the Report outputs of OutJob files:

* `Installation\MyExtension.out` declares the outputer: AD creates it with `InvokeOutputGenerator` of
  `CSharpPlugin.PluginFactory` in `MyExtension.dll`.
* `MyExtension\ReportOutput.cs` is the outputer. It is part of the shim and stays stable: it decides the file name
  (`[target prefix]<project>.txt` in the output folder, or the OutJob's own output name) and hands the project to
  the implementation. AD may run it before any other command of the extension; it starts the extension itself.
* `MyExtension.Impl\Commands\ReportCommand.cs` writes the content: the components and nets of the project. Like any
  command it hot-reloads in Debug. `tools/dev.ps1 Report` writes the focused project's report to `%TEMP%` without
  running the OutJob.

In an OutJob: **Add New Report Output › MyExtension Report**, data source **[Project]**, link it to an output
container (e.g. **Folder Structure**), then **Generate content**. When the content command fails, AD shows the
error (a dialog, except during a release) and adds it to the Messages panel. Changing the `.out` file, or the
outputer in the shim, needs a redeploy and an AD restart; changing the content doesn't.

The outputer derives from `Altium.Edp.Classes.OutputGenerator` in `Altium.SDK.dll`, the base class Altium Designer's
own .NET outputers use. It is not covered by the SDK reference yet: treat it as a preview.
<!--#endif -->
<!--#if (Panel) -->

## Panel

The extension has a dockable **MyExtension** panel (**MyExtension › Show Panel**, the `Panel.Show` command, or AD's
Panels button). AD first opens it floating; dock it where you like, and AD remembers the place.

* `Installation\MyExtension.ins` declares it (`PanelInfo`, name `MyExtensionPanel`).
* `MyExtension\Panel.cs` is the panel's form and view in the shim, registered in `PluginServerModule.RegisterViews`.
  It stays an empty host control, published in AppDomain data together with a command invoker.
* `MyExtension.Impl\Ui\MyExtensionPanel.cs` docks the content into that host when the panel is first shown
  (`Panel.Attach`) and after every hot reload (`HotEntry.OnLoad`). `HotEntry.OnUnload` removes it, so the old
  generation can unload.
* `MyExtension.Impl\Ui\PanelContent.cs` is the content: the extension's commands, run on a double-click. Replace it
  with your own UI, and keep in mind:
  * Run commands with `MyExtensionPanel.Invoke` (through the shim, like the menus), not `CommandRegistry.Execute`:
    in Debug the hot-reload host then never swaps the implementation while one of its commands runs.
  * The content is rebuilt on every reload. Keep state that must survive one outside the control, as BCL types
    (for example JSON in AppDomain data, never your own types).
  * Remove what the content subscribes to outside itself (timers, `Application.Idle`, events of Altium or shim
    objects) in `MyExtensionPanel.Detach`; disposing the control releases only its own child controls.
  * Lay it out with docking or a `TableLayoutPanel` rather than fixed pixel sizes, which don't follow the display
    scaling. Use `DxpThemeManager` colours so it reads well in AD's light and dark themes (they are read when the
    content is built: a theme change shows after the next reload or restart).

Changing `PanelInfo` or `Panel.cs` needs a redeploy and an AD restart; changing the content doesn't.
<!--#endif -->
<!--#if (Tests) -->

## Tests

`dotnet test` runs `MyExtension.Tests` without AD. The Altium SDK assemblies are referenced for compilation only, and
no Altium object exists outside AD: test logic that doesn't touch them. A good pattern is to read what a command
needs from AD into plain C# records (a model), and check the model in tests. On ARM64 Windows the (x64) SDK
assemblies can't load at all in a test: types whose signatures use SDK types are then invisible to reflection, so
`CommandRegistry` doesn't list the commands they declare.
<!--#endif -->

## Release

```bash
powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Configuration Release
```

installs `MyExtension.dll`, `MyExtension.Impl.dll` and the installation files (`Installation\*`) without the
Developer menu. The Release shim contains no hot-reload code. Restart AD to switch configuration, and deploy Debug
again to get back to the inner loop.

## Hot-reload package

Debug builds compile in [`Altium.Designer.HotReload`](https://www.nuget.org/packages/Altium.Designer.HotReload),
a source-only package from nuget.org; its README covers the contract, the pipe protocol and the limitations.
`tools/hotreload.ps1` is the package's pipe client.

## Layout

* `MyExtension\`: the shim. `PluginServerModule.cs`, `Commands.cs`, `Implementation.cs`, and `Installation\` (the
  files AD reads: `.ins`, `.rcs`).
<!--#if (Outputer) -->
* `MyExtension\ReportOutput.cs` and `Installation\MyExtension.out`: the OutJob outputer.
<!--#endif -->
<!--#if (Panel) -->
* `MyExtension\Panel.cs`: the panel's host in the shim.
<!--#endif -->
* `MyExtension.Impl\`: the implementation. `Framework\` (HotEntry, CommandContext, CommandRegistry, AltiumSession) and
  `Commands\`.
<!--#if (Panel) -->
* `MyExtension.Impl\Ui\`: the panel content.
<!--#endif -->
<!--#if (Tests) -->
* `MyExtension.Tests\`: offline tests.
<!--#endif -->
* `tools\`: `deploy.ps1`, `dev.ps1`, `ad.ps1`, and `hotreload.ps1` (the pipe client from the package).
* `Directory.Build.props`: Altium paths and shared build settings.
