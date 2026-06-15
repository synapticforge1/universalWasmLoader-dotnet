> **⚠️ PORTABLE PROJECT MEMORY NOW LIVES IN `cmem/`** — start at [`cmem/INDEX.md`](cmem/INDEX.md).
> When saving new project memory, write it into the matching `cmem/` topic file (and refresh its
> pointer in `cmem/INDEX.md`). The **"update the project memory"** and **"look for code issues"**
> triggers are defined in `cmem/INDEX.md` and are binding. This `CLAUDE.md` remains as the auto-loaded
> historical archive; `cmem/` is the source of truth.

# universalWasmLoader-dotnet

Universal WASM loader for .NET.

## Project Overview

Early-stage .NET library/tool for loading WebAssembly (WASM) modules universally within .NET applications.

## Toolchain

- Platform: .NET (C#)
- Build: standard .NET toolchain (`dotnet build`, `dotnet test`, `dotnet run`)
- Package manager: NuGet
- .gitignore: configured for modern .NET projects (covers bin/, obj/, artifacts/, *.nupkg, etc.)
- OS: Windows 11 Pro for Workstations (development environment)

## Repository Structure

Currently initialized with:
- `README.md` — one-line description
- `.gitignore` — standard .NET gitignore

No source code or project files exist yet.

## Notes

- Git safe.directory must be added for this path if running git commands outside Claude Code:
  `git config --global --add safe.directory <repo-path>`
