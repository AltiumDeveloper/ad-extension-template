# Altium.Designer.HotReload

Hot reload for Altium Designer extensions written in C#. Edit your code, build, and the running
Altium Designer swaps in the new code in about 2 seconds, with no reinstall and no restart.
It exists in **Debug builds only**: Release builds contain none of it.

## Why

AD loads an extension DLL once and locks it until AD exits. Without hot reload, every change
means closing AD, reinstalling, restarting it and reopening your project: minutes per iteration.

This package splits an extension into two parts:

```
Altium Designer
 └─ MyExtension.dll        the shim: what AD loads. Tiny, rarely changes.
     └─ MyExtension.Impl.dll   all real code. Debug: hot-reloaded. Release: called directly.
```

In Debug, the shim loads the implementation into a collectible `AssemblyLoadContext`, from bytes
(the file is read into memory and closed at once, so AD never keeps it locked), and loads it again
whenever the build rewrites it or one of its private dependencies. A current-user
named pipe lets a script in a terminal reload, inspect and run commands inside AD.

## Quick start (5 minutes)

You need an AD 24+ extension with the shim and the implementation as two projects. Both target the
.NET version AD hosts (`net8.0-windows` for 26.1, `net10.0-windows` for 26.12), and the shim has
`<UseWindowsForms>true</UseWindowsForms>`. The quickest start is the `dotnet new ad-extension` template
([Altium.Designer.Extension.Templates](https://www.nuget.org/packages/Altium.Designer.Extension.Templates)).

**1. Reference the package and the implementation from the shim:**

```xml
<ItemGroup>
  <PackageReference Include="Altium.Designer.HotReload" Version="1.0.1" PrivateAssets="all" />
  <!-- HotModule="true": hot-reloaded in Debug, a normal reference in Release. -->
  <ProjectReference Include="..\MyExtension.Impl\MyExtension.Impl.csproj" HotModule="true" />
</ItemGroup>
```

**2. Add the entry point to the implementation** (BCL types only, see [Contract](#contract)):

```csharp
namespace MyExtension.Impl;

public static class HotEntry
{
    public static string Invoke(string command, string parameters, IDictionary<string, object> env) =>
        command switch
        {
            "Hello" => $"Hello from generation {env["Generation"]}",
            _ => throw new ArgumentException($"Unknown command '{command}'."),
        };
}
```

**3. Add one dispatcher file to the shim.** This is the only place that knows about Debug and Release:

```csharp
using System.Collections.Generic;

internal static class Implementation
{
#if DEBUG
    private static Altium.Designer.HotReload.HotReloadHost _host;

    // Call once from ServerModule.InitializeCommands (AD's UI thread).
    public static void Start() => _host = Altium.Designer.HotReload.HotReloadHost.Start(
        "MyExtension", Altium.Designer.HotReload.HotReloadBuild.ModulePath);

    public static string Invoke(string command, string parameters, object view) =>
        _host.Invoke(command, parameters, view);
#else
    public static void Start() { }

    public static string Invoke(string command, string parameters, object view) =>
        MyExtension.Impl.HotEntry.Invoke(command, parameters,
            new Dictionary<string, object> { ["Origin"] = "UI", ["Context"] = view });
#endif
}
```

**4. Route AD commands through it.** Register one generic process, for example `MyExtension:Run`
with `Params='Cmd=Hello'`, and call `Implementation.Invoke(cmd, rest, view)` from its handler.
New features are then new commands in the implementation, with no change to the shim or the `.rcs`.

**5. Build Debug, install the shim, and restart AD once.** From now on:

```bash
dotnet build MyExtension.Impl
powershell -File tools/hotreload.ps1 run Hello
```

The build rewrites the DLL, AD swaps it in, and the command runs inside AD. `dotnet watch build`
in the implementation folder reloads on every save.

## Public API

Everything is `internal` and compiled into your shim, in namespace `Altium.Designer.HotReload`.

| Member | Purpose |
|---|---|
| `HotReloadHost.Start(string name, string modulePath)` | Create the host on AD's UI thread. Starts the file watcher and the pipe `<name>.HotReload.<pid>`. The module loads on first use. |
| `HotReloadBuild.ModulePath` | Generated constant: full path of the `HotModule="true"` project's Debug output. |
| `string Invoke(string command, string parameters, object context)` | Run a command (env `Origin` = `"UI"`). Reloads first if the file changed. |
| `IReadOnlyList<string> GetCommands()` | The module's `GetCommands()`, or empty. |
| `string Reload()` | Load again even if unchanged. |
| `string Status(bool collect)` | Diagnostics text. `collect: true` forces a GC first. |
| `int Generation`, `string LastError`, `string Name`, `string ModulePath`, `string PipeName`, `string LogPath` | State. |
| `const int ContractVersion` | Version of the `HotEntry` contract (currently `1`). |
| `HotReloadException` | A build was rejected. The previous generation keeps running. |
| `void Dispose()` | Stop the watcher and the pipe, unload the module. |

Call every member on AD's UI thread. AD calls your commands there, so this is the default.

## Contract

The implementation exposes a public static class named `HotEntry`, in any namespace. All its
signatures use BCL types only, so the shim never needs rebuilding when the implementation evolves.

```csharp
public static class HotEntry
{
    public static string   Invoke(string command, string parameters, IDictionary<string, object> env); // required
    public static string[] GetCommands();                         // optional: "Name\tDescription" per command
    public static void     OnLoad(IDictionary<string, object> env); // optional: after a generation loads
    public static void     OnUnload();                            // optional: before it unloads; release everything
}
```

`env` keys (contract version 1). Read them defensively: in Release, the shim passes only what it
chooses (typically `Origin` and `Context`).

| Key | Type | Value |
|---|---|---|
| `Origin` | `string` | `"UI"` (AD process), `"Pipe"` (terminal), `"Host"` (`OnLoad`) |
| `Context` | `object` | the `IServerDocumentView` AD passed, or null |
| `Log` | `Action<string>` | pipe: streams a line to the terminal; if kept and called after the request ended, it appends to the log file instead. UI: appends to the log file. Never throws, callable from any thread. May be absent. |
| `Generation` | `int` | 1, 2, 3… per successful load |
| `ContractVersion` | `int` | `HotReloadHost.ContractVersion` |

`parameters` is passed through unchanged. AD style is `Key=Value|Key=Value`.

Module code may pump messages (a modal dialog, `Application.DoEvents`) in any of these methods.
While `Invoke`, `GetCommands`, `OnLoad` or `OnUnload` runs, a reload (file change, `reload`) is
deferred and runs once the outermost call completes; the code is never swapped under a running
call. A command re-entered while a generation is being swapped (from `OnLoad`/`OnUnload`) fails
with a `HotReloadException` asking to try again.

## Debug vs Release

| | Debug | Release (any configuration other than `Debug`) |
|---|---|---|
| Package sources | compiled into the shim | not compiled |
| `HotModule="true"` reference | built, not referenced, not copied | normal reference, copied next to the shim |
| Shim calls the implementation | through `HotReloadHost` (collectible ALC) | directly: `HotEntry.Invoke(...)` |
| Watcher, pipe, `AssemblyLoadContext` | yes | **none** |

The switch is `'$(Configuration)' == 'Debug'` in MSBuild, matching `#if DEBUG` in C#. Check a
Release shim yourself: its metadata contains no `NamedPipeServerStream`, `AssemblyLoadContext`
or `FileSystemWatcher`. In Release, call `HotEntry.OnLoad`/`OnUnload` from the dispatcher if
your implementation relies on them.

**Why a source-only package?** All extensions share AD's default load context. If two extensions
each shipped `Altium.Designer.HotReload.dll`, the first one loaded would win and the other would
run the wrong version. Compiled-in `internal` types cannot collide, need no extra DLL in the
install folder, and make "no hot reload code in Release" a matter of not compiling it.

## The pipe

`\\.\pipe\<name>.HotReload.<pid>` exists only while a Debug shim runs in AD process `<pid>`.

**Client:** `tools/hotreload.ps1`, shipped in the package
(`%USERPROFILE%\.nuget\packages\altium.designer.hotreload\<version>\tools\`). Copy it into your repo.

```bash
powershell -File tools/hotreload.ps1 ping
powershell -File tools/hotreload.ps1 status -gc
powershell -File tools/hotreload.ps1 reload
powershell -File tools/hotreload.ps1 list
powershell -File tools/hotreload.ps1 run MyCommand "Key=Value|Other=1"
powershell -File tools/hotreload.ps1 -Name MyExtension -Pid 12345 status
```

With one instance running, the script picks it. With several (extensions or AD processes), it
lists them and exits with 2. Exit codes: 0 ok, 1 command failed, 2 usage or ambiguous instance,
3 no instance reachable (or no answer in time). The script is easy to call from build scripts and AI agents.

The verb is checked locally first: an unknown verb, or `run` without a command, exits with 2
without looking for AD; `help` (or no arguments) prints the verbs and exits with 0.
`-TimeoutMs` (default 3000) limits connecting. `-ReadTimeoutMs` (default 30000) limits the wait
for each line of the answer, because AD's UI thread may be busy (a modal dialog, a long command):
on timeout the script exits with 3. It applies to every verb except `run`, which waits as long as
the command takes unless you pass `-ReadTimeoutMs`. `0` means no limit.

**Protocol:** one request per connection. The client writes one UTF-8 line,
`<verb> [arguments]`. The server streams output lines, then a final line of `U+0004` followed by
the exit code. Verbs: `ping`, `status [-gc]`, `reload`, `list`, `run <command> [parameters]`.

**Security:** the pipe is created with `PipeOptions.CurrentUserOnly`, so only processes of the
same Windows user can connect. That user can run any command of your extension inside AD. It
exists in Debug builds only. Never ship a Debug shim.

If the pipe cannot be created, the host retries with a backoff (1 s doubling up to 30 s), logs
each distinct failure once, and `status` (called from AD) shows it as `Pipe error`.

## Unload pitfalls

An old generation is freed only when nothing references it. `status -gc` reports generations that
survived a full GC ("Pending"). Common causes, all in your implementation:

* Subscribing to static or host events (AD objects, `Application.Idle`, `AppDomain`) without
  unsubscribing in `OnUnload`.
* Leaving forms open, or running timers or threads. Close and stop them in `OnUnload`.
* The shim holding objects created by the implementation. Pass strings across the boundary.
* Framework reflection caches, such as System.Text.Json's. The host handles these: before every
  unload it calls each `[MetadataUpdateHandler]` `ClearCache` hook, the mechanism .NET Hot
  Reload uses.

Don't cache Altium COM objects across commands. Query the workspace again each time.

**Known limitation: WPF keeps the first load of each new build (accepted).** AD hosts WPF, which
subscribes to every assembly load. `System.Xaml.XamlSchemaContext` queues each newly loaded
assembly in a private list, and `SafeSecurityHelper` caches its name in a static dictionary.
Both hold the assembly strongly, so the **first** load of each new build is never collected.
Loading bytes AD has seen before unloads fine. The cost is about one module's size per rebuild
(usually well under 1 MB). It clears when AD restarts, and `status -gc` shows it as "Pending".
Your code doesn't cause it, and fixing it would mean editing WPF internals by reflection, so the
package deliberately leaves it alone. Release loads the module once, so it doesn't apply there.

## Troubleshooting

| Symptom | Fix |
|---|---|
| The extension's menu and commands are missing after an AD restart | AD loads only extensions listed in `Extensions\ExtensionsRegistry.xml`. Register it there (the `ad-extension` template's `deploy.ps1` does), then restart AD. |
| `No hot reload instance found` | AD starts extension servers on first use, so the pipe exists only after the extension's first command in an AD session: run any of its commands once. Otherwise AD isn't running a **Debug** shim, or it isn't deployed yet: deploy the Debug build (the template's `tools\deploy.ps1`) and restart AD once. |
| `No response from \\.\pipe\... within N ms` (exit 3) | AD's UI thread is busy: a modal dialog is open or a command is still running. Close the dialog and retry, or raise `-ReadTimeoutMs`. |
| `Several instances are running` | Add `-Name` and/or `-Pid` as listed. |
| `Implementation module not found. Build it first.` | Build the implementation (Debug). `status` shows the expected path. |
| `Build … rejected: No public static class 'HotEntry'` | The class must be `public static` and named `HotEntry`; `Invoke` must match the contract exactly. |
| A build doesn't seem to load | `status` shows the generation, hash and last error. The log (`%LOCALAPPDATA%\Altium.Designer.HotReload\<name>.log`) has every load, rejection and pipe request. |
| `Pending: generation(s) …` after `status -gc` | See [Unload pitfalls](#unload-pitfalls). A few pending generations after many rebuilds is the WPF limitation. |
| `mark exactly one ProjectReference with HotModule="true"` | Mark exactly one ProjectReference in the shim with `HotModule="true"`. |
| A type from `Altium.*` is different between shim and module | Altium assemblies AD provides are always shared with AD. Don't copy them to the module output: reference them with `<Private>false</Private>`. |
| A change in a referenced project or package doesn't load | Changes to `*.dll` / `*.deps.json` directly in the module folder reload; files only under subfolders don't: run `hotreload.ps1 reload`. A changed native DLL needs an AD restart (see [Dependencies](#dependencies)). |
| The repo moved | `HotReloadBuild.ModulePath` is baked in at build time: rebuild and redeploy the Debug shim. |

## Dependencies

Other dependencies of the implementation (NuGet packages, other projects) load privately into
each generation:

* They are located with the module's `.deps.json` (`AssemblyDependencyResolver`), so RID-specific
  assets (`runtimes\<rid>\lib\...`), satellite assemblies and native DLLs (`runtimes\<rid>\native\...`)
  are found. Without a `.deps.json` the module folder is probed (`<name>.dll`).
* Managed assemblies are read into memory (from bytes) and never locked: the build can overwrite them.
* **Native DLLs** cannot be loaded from memory: they load from their path, stay **locked**, and stay
  loaded until AD exits (Windows never unloads them with the load context). Rebuilding a native
  dependency needs an AD restart.
* `Altium.*` assemblies that AD provides (already loaded, or found by AD's load context) are always
  shared with AD so COM types unify. An `Altium.*` assembly that only exists next to the module
  (your own library) loads privately like any other dependency.
* The module reloads when the module DLL **or** a `*.dll` / `*.deps.json` directly in its folder
  changes (name, size or write time), even if the module itself was not rewritten. Changes only
  under subfolders (`runtimes\...`, satellite folders) are not detected: run `hotreload.ps1 reload`.

The host reads each file with `FileShare.Read | FileShare.Delete`: while the build still has a
file open for writing, the read waits and retries (up to about 2 s) instead of loading a
half-written image. If it stays locked, the running generation keeps running and the reload
happens when the writer closes the file (a first load or an explicit `reload` reports the error
instead). The file is held open only while it is read.

## Building this package

```bash
dotnet test tests/Altium.Designer.HotReload.Tests
dotnet pack -c Release
```

The tests host the real `HotReloadHost` on an STA thread outside AD, with modules compiled on
the fly. They check load, swap and collection of the old generation, rejection of a bad build,
reload on file change, a pipe round trip through `tools/hotreload.ps1`, and the 1.0.1 regressions
(`RegressionTests.cs`): a kept pipe `Log`, reloads re-entered from a pumping `OnUnload`, a DLL still
being written, private / `Altium.*` / RID-specific dependencies and dependency-only changes, pipe
creation backoff, and the client's verb validation and read timeout.

## License

MIT (see `LICENSE`), Copyright (c) 2026 Altium Limited.
