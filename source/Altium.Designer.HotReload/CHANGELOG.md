# Changelog

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
* Works in Altium Designer hosting .NET 8 (26.1) and .NET 10 (26.12).
