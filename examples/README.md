# Examples

Complete extensions generated with `dotnet new ad-extension`, each with its own README:

| Example | What it adds to the generated extension |
|---|---|
| [BoardTools](BoardTools/README.md) | `BoardSummary`: counts components, nets, tracks, vias, pads and arcs on the open board and posts the result to the Messages panel. The smallest real command. |
| [DesignChecks](DesignChecks/README.md) | A decoupling ERC (focused project) and DRC (open board) that publish to the Messages panel with cross-probing, plus offline unit tests over synthetic designs and JSON snapshots (`dotnet test DesignChecks.Tests`). |

Like any generated extension, each one builds against the Altium Designer installation named in its
`Directory.Build.props` (`AltiumSystemDir`, default `C:\Program Files\Altium\AD26\System`). Adjust it to your machine
if needed, then follow the generated README: `tools\deploy.ps1`, one AD restart, `tools\dev.ps1`.

To regenerate an example after a template change: generate a new extension with the same name (`--tests false` for
BoardTools), keep the example's GUIDs (`[assembly: Guid]`, `ExtensionGuid`, the `.sln` project GUIDs), and copy over
its own files: `BoardTools.Impl\Commands\BoardSummaryCommand.cs` and the `.rcs`/`.ins`; for DesignChecks, the
`DesignChecks.Impl` files outside `Framework\` plus `Framework\PcbSession.cs`, the `DesignChecks.Tests` files other
than `CommandTests.cs`, and the `.rcs`/`.ins`. DesignChecks' `HotEntry.OnUnload` closes its palette and its
`AltiumSession` has an extra `IsFreeDocuments`. Finally restore the "Example" note at the top of each README.
