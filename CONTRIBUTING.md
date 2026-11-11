# Contributing

Issues and pull requests are welcome. For anything larger than a fix, please open an issue first to
discuss the change.

## Building and testing

You need Windows, the .NET 8 and .NET 10 SDKs, and, to build the template output and the examples,
an Altium Designer installation (they reference `Altium.SDK.dll` from it).

```bash
# Hot reload host: tests on .NET 8 and .NET 10 (no Altium Designer needed)
dotnet test source/Altium.Designer.HotReload/tests/Altium.Designer.HotReload.Tests -c Release

# Both packages
dotnet pack source/Altium.Designer.HotReload -c Release -o artifacts
dotnet pack source/Altium.Designer.Extension.Templates -c Release -o artifacts

# Try the template (with every option on), building against a local feed with the new HotReload package
dotnet new install artifacts/Altium.Designer.Extension.Templates.1.0.1.nupkg --force
dotnet new ad-extension -n TryIt --panel --outputer -o ../TryIt
dotnet nuget add source "$PWD/artifacts" --name local --configfile ../TryIt/nuget.config
dotnet build ../TryIt -p:AltiumSystemDir="C:\Program Files\Altium\AD26\System"
dotnet test ../TryIt/TryIt.Tests
```

Before submitting, check the change in Altium Designer (deploy with `tools/deploy.ps1`, then
`tools/dev.ps1`), in Debug and in Release.

## Notes

* `source/Altium.Designer.Extension.Templates/content/ad-extension/tools/hotreload.ps1` is a copy of
  `source/Altium.Designer.HotReload/tools/hotreload.ps1`. Keep them identical; CI checks it.
* The examples are generated with the template. When the template's shared files change (`Framework\`, `tools\`,
  `Directory.Build.props`, the shim), apply the same change to the examples.
* Nothing in this repository may depend on a particular machine: no absolute paths other than the
  documented Altium Designer defaults, and no design files or snapshots of designs you may not publish.

## CI

[`ci.yml`](.github/workflows/ci.yml) runs on every push and pull request, on `windows-latest`. GitHub's runners
have no Altium Designer, so CI builds and tests the hot reload host, packs both packages, generates extensions
with each template option, and checks the generated files; it does not compile code that references the
Altium SDK. Build that locally against an installation.

## Releasing

Each package is released on its own, from a tag; the version comes from the tag:

| Package | Tag | Workflow |
|---|---|---|
| `Altium.Designer.HotReload` | `hotreload-v1.2.3` | [`release-hotreload.yml`](.github/workflows/release-hotreload.yml) |
| `Altium.Designer.Extension.Templates` | `templates-v1.2.3` | [`release-templates.yml`](.github/workflows/release-templates.yml) |

1. Update the package's `CHANGELOG.md` (HotReload) or README, and merge to `main`.
2. When HotReload changes, release it first, then bump the `Altium.Designer.HotReload` version in
   `content/ad-extension/MyExtension/MyExtension.csproj` and in the examples, and release the templates.
3. Create a GitHub release with a new tag (`gh release create hotreload-v1.2.3 --generate-notes`), or push the tag.
   The workflow tests, packs, publishes to nuget.org with [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
   and attaches the `.nupkg` to the GitHub release.

Publishing needs the repository secret `NUGET_USER` (the nuget.org profile, `Altium`), and a trusted publishing
policy on nuget.org for this repository and each release workflow file.
