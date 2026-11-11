# Changelog

## 1.0.1

* The pipe's `Log` delegate (env `Log` of a `run`) no longer throws when the module keeps it and
  calls it after the request ended, possibly off the UI thread (which could crash AD). From then
  on it appends to the host log.
* Re-entrancy: `OnLoad`, `OnUnload` and `GetCommands` now count as running module code, like
  `Invoke`. A reload triggered while one of them pumps messages (modal dialog, `DoEvents`) is
  deferred and runs once the outer operation completes. Previously it ran re-entrantly: wrong
  generation bookkeeping, `OnUnload` called twice, a leaked context, the newest build never
  loaded. A command re-entered during a generation swap fails with "try again".
* A DLL still being written is no longer read half-written and rejected as a bad image: files
  are opened with `FileShare.Read | FileShare.Delete` (the open retries while a writer has the file
  open), still read into memory and closed at once. If the file stays locked, the running
  generation keeps running and the watcher reloads when the writer closes the file.
* Dependency resolution uses the module's `.deps.json` (`AssemblyDependencyResolver`):
  RID-specific assets, satellite assemblies and native DLLs (`LoadUnmanagedDll`) are found, with
  `<moduleDir>\<name>.dll` as fallback. Managed dependencies are still loaded from bytes; native
  DLLs load from their path (locked until AD exits). `Altium.*` assemblies are shared with AD only
  if AD provides them; an `Altium.*` library that exists only next to the module is no longer
  rejected.
* A change to a private dependency (`*.dll` or `*.deps.json` directly in the module folder) now
  reloads the module even if the module DLL itself was not rewritten.
* The pipe server retries a failing pipe creation with a backoff (1 s doubling to 30 s) instead
  of every second, logs each distinct failure once, and `status` shows it as `Pipe error`.
* `tools/hotreload.ps1`:
  * instance discovery no longer fails on Windows PowerShell 5.1 when another process has a pipe
    whose name contains characters illegal in paths;
  * the verb is validated locally: an unknown verb or `run` without a command exits with 2
    without looking for AD; `help` (or no arguments) prints the verbs locally and exits with 0;
  * new `-ReadTimeoutMs` (default 30000, `0` = none): a busy UI thread no longer hangs the client
    forever, it exits with 3 and says so. Not applied to `run` unless given explicitly;
  * "No hot reload instance found" mentions that the extension may not be deployed yet
    (`tools\deploy.ps1`, then restart AD).
  * Exit codes unchanged: 0 ok, 1 command failed, 2 usage or ambiguous instance, 3 AD not reachable.

## 1.0.0

First public release.

* Source-only package: compiled into the shim as `internal` types, in Debug builds only.
* `HotReloadHost`: collectible `AssemblyLoadContext` per generation, loaded from bytes; atomic
  swap (a bad build keeps the old generation); reload on file change; framework cache clearing
  before unload.
* `HotModule="true"` ProjectReference: built but not referenced in Debug, a normal reference in
  Release. Its output path is generated as `HotReloadBuild.ModulePath`.
* Dev pipe `<name>.HotReload.<pid>`, current user only, so several AD instances can run side by side.
* `tools/hotreload.ps1` client: `ping | status [-gc] | reload | list | run`, picks the instance
  automatically or takes `-Name`/`-Pid`.
* `HotEntry` contract version 1.
* Pipe `run` fails (exit 1, "Build … rejected") while the build on disk is rejected, even when the
  file watcher saw it first.
* Supports both runtimes Altium Designer hosts: .NET 8 and .NET 10.
