# Security Policy

This policy covers the packages built from this repository: `Altium.Designer.HotReload` and
`Altium.Designer.Extension.Templates` on NuGet, and the code they generate.

## Reporting a vulnerability

If you believe you have found a security vulnerability, please report it privately
rather than opening a public issue.

- Use GitHub's [private vulnerability reporting](https://github.com/AltiumDeveloper/ad-extension-template/security/advisories/new)
  ("Report a vulnerability" under the repository's **Security** tab), or
- Email the maintainers at **security@altium.com**.

Please include enough detail to reproduce the issue (affected package and version, a
minimal example, and the impact). We aim to acknowledge reports within a few business days.

## Scope

The hot reload host exists for development. A few things are intentionally **out of scope**:

- **Debug builds in production.** A Debug shim opens a named pipe through which any process of the
  current Windows user can run the extension's commands inside Altium Designer, and it loads the
  implementation from its build folder. That is its purpose. Distribute Release builds only; they
  contain no hot reload code.
- **Code an extension runs.** Generated extensions run with Altium Designer's access to your designs
  and files. What your own commands do with it is up to you.
