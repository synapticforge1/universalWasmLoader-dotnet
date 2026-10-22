# Working rules — universalWasmLoader-dotnet

Repo-specific rules. The workspace floor (`../../cmem/working-rules.md`) still applies underneath.

## Scripting is C#, not shell (owner, 2026-10-22)

- 🔒 **Owner's order of preference (2026-10-22): the repo language's own scripting where it has one;
  Bun/TypeScript only where it does not.** C# has it (.NET 10 file-based apps), so Bun is not used
  here. Filed workspace-wide as `../../cmem/divergences.md` P6 (2026-10-22, class DESIGN); the
  workspace `working-rules.md` Tooling rule 1 now states the same precedence, with a per-language
  table whose C# row is `dotnet10 run --file <file>.cs` (owner, 2026-10-22). This session
  also measured all 12 non-TS repos against that table (2026-10-22): the "P6 measurement" table
  under P6 in `../../cmem/divergences.md` (6 conform, -dotnet among them; 6 do not).
- 🔒 **Scope (owner, final 2026-10-22; workspace Tooling rule 1):** the rule governs **COMMITTED
  scripts kept for repeated use** that would otherwise be a `.ps1`/`.sh`/`.cmd` (fetch, build-helper,
  generator, CI-logic). Those are C#. Product code and the test suite are not scripts; project tasks
  stay plain `dotnet10 build` / `test` / `pack`. **A one-time script deleted after use may be
  PowerShell, bash, Bun or C#**, whichever fits, in the session scratchpad — still WRITTEN with
  `Write`, never a heredoc. If it is needed again, it is promoted to C# before it is committed.
  (Superseded the same day: an interim "C# scratchpad-only, never committed" reading.)
- **No committed shell scripts.** CI YAML `run:` steps only launch commands.
- **Where a committed C# script lives:** none exists yet. When the first one lands, put it in
  `scripts/` and, in the same commit, add `scripts/**` `Remove` lines (`Compile`, `Content`, `None`)
  beside the `tests/**` ones in `UniversalWasmLoader.csproj`. The root csproj compiles every `.cs`
  below it, so without them the script is built into the NuGet package and breaks the build (two sets
  of top-level statements). Verified 2026-10-22 with a probe: with the exclusion, the Release build
  had 0 errors and `dotnet10 run --file scripts/probe.cs` ran (the exclusion was then reverted, as no
  script existed).
- **Porting a TypeScript script to C#:** it replaces the TS original only once proved equivalent
  (workspace rule 1): same inputs, diff the outputs, one input that must fail, comparison in the
  commit.
- **How to run one:** a C# file-based app (.NET 10+; NuGet deps via `#:package Name@version` at the
  top of the file), run with `dotnet10 run --file <path>/<name>.cs`. **`--file` is required**
  whenever the shell's current directory is the repo: plain `dotnet10 run <path>.cs` sees
  `UniversalWasmLoader.csproj` in the current directory, runs the library project instead and asks
  for a `--framework`, even when the `.cs` is outside the repo (verified 2026-10-22, SDK 10.0.402).
  **Use `dotnet10`, never bare `dotnet`** (owner, 2026-10-22): bare `dotnet` has no SDK on the
  development machine; see [overview.md](overview.md) → "Toolchain".
- **Letter H15 (workspace `handoffs.md`) — this repo's side DONE 2026-10-22.** Asked: record the
  final scope and the `dotnet10` command; nothing to port. Recorded above. Checked against the tree
  at `f81cb55`: `git ls-files` has no `.ps1`/`.sh`/`.bat`/`.cmd`/`.ts`/`.mjs`/`.js`/`.py` and no
  `.cs` beyond the five library sources; `git grep -i -E 'deno|\bbun\b|\bnode\b|npx'` matches nothing (the word boundaries matter: without
  them, `bun` matches `ubuntu-latest` at `publish.yml:12`, a false hit; corrected 2026-10-22); the four
  `run:` steps in `publish.yml` each launch one `dotnet` command (restore, build, pack, nuget push),
  so CI holds no script logic. The workspace session closes the letter.
- **Status 2026-10-22:** no committed scripts; nothing referenced Deno; CI (`.github/workflows/publish.yml`) uses only
  `dotnet` commands.

## Line endings are LF, never CRLF (owner, 2026-10-22)

- **Every text file is LF, in the repo and in the working copy**, following GitHub's preference. This is
  enforced by the repo itself, not by anyone's machine config:
  - `.gitattributes`: `* text=auto eol=lf` (checkouts get LF even on Windows with
    `core.autocrlf=true`); `*.wasm`, `*.png`, `*.snk` are `binary`.
  - `.editorconfig`: `end_of_line = lf` for `[*]`, so VS / VS Code / Rider save new files as LF.
- **Adding a new binary type** (fixtures, images, keys): add a `binary` line to `.gitattributes` in
  the same change, or git may rewrite its bytes.
- **Checking:** `git ls-files --eol` must show `i/lf w/lf` on every text file. If a file was edited
  as CRLF, `git add --renormalize .` fixes the index.
- **Status 2026-10-22:** the index was already all LF; `.gitignore` and `LICENSE` were CRLF on disk
  and were converted.
