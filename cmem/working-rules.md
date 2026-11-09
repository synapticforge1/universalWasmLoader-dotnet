# Working rules — universalWasmLoader-dotnet

Repo-specific rules. The workspace floor (`../../cmem/working-rules.md`) still applies underneath.

## Committed scripts are C# scripts run by dotnet-script (owner, 2026-11-08)

- 🔒 **The rule (owner, 2026-11-08: "yes. move them"):** committed scripts are `scripts/*.csx`,
  run by **dotnet-script 2.0.1, pinned as a LOCAL tool in `dotnet-tools.json`** (repo root,
  `rollForward: false`). Run them as **`dotnet10 tool run dotnet-script scripts/<name>.csx
  [-- args]`**. Once per clone, run `dotnet10 tool restore`. CI does the same with the runner's
  `dotnet` (`dotnet tool restore`, then `dotnet tool run dotnet-script …`), in the token-less
  jobs only. Today's scripts: `fixtures.csx`, `runtimes.csx` (uses `#r "nuget: YamlDotNet,
  18.1.0"`, MIT, pinned), `compat.csx`.
- **Why it fits workspace Tooling rule 1** ("no mechanism that BUILDS AN EXECUTABLE"): measured
  2026-11-08 on SDK 10.0.403. dotnet-script compiles a script to ONE cached `.dll` under
  `%TEMP%\dotnet-script\<the script's path mirrored>\` and runs it inside the tool's own
  process; no `.exe` is built per script. Its restore takes ~13 s on the first run; later runs
  of an unchanged script take ~1 s. By contrast, `dotnet10 run --file x.cs` builds `x.exe` (an
  apphost) and runs as it; only with `#:property UseAppHost=false` would it build a `.dll`
  alone. That route was not taken. `dotnet10 repl` is not an SDK command (it would be the
  separately installed `dotnet-repl`). The workspace table's row changes from "C# → Bun" to "C#
  → `.csx` via dotnet-script" (the workspace session's file, reported in ledger row C32).
- 🔒 **Never the bare shim `dotnet-script <file>`** (owner, 2026-11-08: it "is throwing errors
  in the terminal when used"). **And not the GLOBAL form `dotnet10 script <file>` either, from a
  directory on `D:`** (measured 2026-11-08). From a `D:` working directory, the SDK hands a
  global tool to the bare `dotnet-script.exe` shim (`Environment.ProcessPath` =
  `~\.dotnet\tools\dotnet-script.exe`). From the scratchpad on `C:`, it hosts the tool in its own
  `dotnet.exe`. dotnet-script then restores with a plain `dotnet restore … -r win10-x64`,
  resolved like any child process. From the shim, that finds the SDK-less system `dotnet` first
  on PATH, and the restore fails ("Unable to restore packages"). With the SDK first on PATH it
  passes, since the generated project's `UseRidGraph=true` makes `win10-x64` valid. This is the
  owner's bare-shim error, and very likely the 2026-10-22 failure too. A LOCAL tool always runs
  inside the SDK's `dotnet.exe`, from any directory, so its restore finds the SDK. (The
  2026-10-22 MAX_PATH overflow was for a scratchpad-path script; the repo's longest cache path
  is well under the limit.)
- **The port (2026-11-08), under the workspace port rule** (a port replaces its original only
  after an output diff proves them equivalent). Each `.csx` reproduces its `.ts` line for line,
  checked by running both on the same inputs (one-time harness, deleted):
  - `fixtures`: the real `tests/`, an explicit dir, a broken copy (a flipped byte, a stray
    `.wit`, a missing `.wat`); identical output and exit code. A dir without `fixtures.json`
    crashes both (exit 1, an unhandled error naming the file; stack text differs by runtime).
  - `runtimes`: `selftest`, the live check, and two failure mutants applied to both (tests
    `net9.0` only + `validate.yml` 9.0.x; `LEAVING_DAYS` 31): identical.
  - `compat`: `selftest` / `surface --check` / `report` in the repo. Then two throwaway clones
    (one Bun, one `.csx`, the same steps): bump refused on a dirty tree, release on an untagged
    HEAD, an allowed `bump patch` (the resulting `git diff` identical), `release v0.2.1`, a stale
    surface (`surface --check` and `report`), red previous-tag tests (`report`, `bump patch`
    REFUSED), an unknown command. Every verdict, exit code and diff is identical. The only text
    differences are random temp-folder names and xUnit's elapsed-time stamp in the red-test tail.
  - The usage lines name their own file (`compat.csx`, `runtimes.csx`): intended.
  - Port details that matter: an insertion-ordered set where the Bun code relied on JS `Set`
    order; `StringComparer.Ordinal` for JS's default sort; `[0-9]` for `\d` (.NET's `\d` is any
    Unicode digit); UTF-8 console output (`→`, `⚠️`); child output = all stdout then all stderr,
    as `Bun.spawnSync` gave it; `#nullable enable` in every script (dotnet-script compiles with
    annotations off).
- **History:** 2026-10-22 "language first" (C# file-based apps) → "committed scripts only" →
  "no mechanism that builds an executable" (C# → Bun): the gate was written as
  `scripts/compat.cs` (`51c447f`, never pushed), then ported to `compat.ts` (`0874746`). On
  2026-10-22 dotnet-script 2.0.1 failed here (`NETSDK1083` on `-r win10-x64`, and MAX_PATH), so
  Bun stayed. On 2026-11-08 the owner re-asked and kept Bun "for now" ("the scripting is still
  not working yet"), then corrected it ("it does work"), and said "move them". Workspace P6 in
  `../../cmem/divergences.md`.
- **Scope:** the rule governs COMMITTED scripts kept for repeated use (what would otherwise be a
  `.ps1`/`.sh`/`.cmd`). Product code and the test suite are not scripts; project tasks stay plain
  `dotnet10 build` / `dotnet10 test tests` / `dotnet10 pack`. **A one-time script deleted after use
  may be PowerShell, bash, Bun or anything else that fits**, in the session scratchpad, still
  WRITTEN with `Write` (never a heredoc); ported to `.csx` before it is committed.
- **No committed shell scripts.** CI YAML `run:` steps only launch commands.
- 🔒 **No `gh`, in CI too** (workspace Tooling rule 9, letter H30; owner, 2026-11-08: "it has been
  found to be more complicated to use than it is worth", "yes change those"). Check the repo
  instead: `git ls-remote origin`, `git fetch`, the repo's files, a registry's public API
  (nuget.org's flat-container), GitHub's public REST API for public data. A task only GitHub's
  web UI can do (a Release page by hand, run logs, a Support request) is an OWNER item. Never
  write "blocked on gh". In workflows, GitHub's REST API with the job's `GITHUB_TOKEN`, from this
  repo's script mechanism: the `release` job runs `scripts/github-release.csx` (2026-11-09;
  [overview.md](overview.md) → "Build / release flow").
- 🔒 **Every release gets its GitHub Release page made BY CI** (letter H32, owner 2026-11-09:
  "the release page should be referred back to the workspace to insure that all projects have
  this setup"; "only new tags will get release pages", so no backfill). Never by hand, never the
  GitHub CLI. Here: `publish.yml`'s `release` job, `contents: write` only, after the nuget.org
  push, running `scripts/github-release.csx` in H32's order: (1) the tag peels to the released
  commit; (2) an existing page = done, no write; (3) POST with the nuget.org link on top + attach
  + read back + peel again; a 30 s limit per request. A change to it lands under the gate before
  a release, never during one.
- **Bun versions (workspace Tooling rule 10, letter H31, owner 2026-11-09): record-only here.**
  This repo runs no Bun since 2026-11-08 (scripts are `.csx`; CI has no `setup-bun`), so it has
  no Bun pin to move. A dated measurement that names a Bun version stays as it was measured. If
  Bun ever returns, every notation is the latest installed Bun.
- **`.csx` needs no csproj exclusion:** the SDK compiles only `.cs`.
- **Use `dotnet10`, never bare `dotnet`** (owner, 2026-10-22; reason restated by the owner
  2026-11-08): on the development machine bare `dotnet` is .NET 8, kept because installed
  programs depend on that version, and `dotnet10` is .NET 10. This repo is .NET 10 only from
  0.2.0. Never change what bare `dotnet` points at. See [overview.md](overview.md) → "Toolchain".
  `compat.csx` picks `$UWL_DOTNET`, else `dotnet10` when on PATH, else `dotnet` (CI).
- **`dotnet11` is the .NET 11 SDK** on the development machine (added 2026-11-08 at the owner's
  request, the same kind of command as `dotnet10`; 11.0.100-rc.3 that day). Use it only for
  .NET 11 work (the `net11.0` test target at GA: [open-work.md](open-work.md)); the repo's
  commands stay on `dotnet10`.
- 🔒 **`DOTNET_ROOT` stays the .NET 8 system install** (owner, 2026-11-08: "I have to keep the
  dotnet root as dotnet 8 for installed programs that are not yet compatible with dotnet 10").
  Never change it, and never set it for this repo's work. `dotnet10` / `dotnet11` find their own
  SDKs without it. With it on the .NET 8 install, this repo's build, 59/59 tests and `dotnet10
  tool run dotnet-script` all pass (2026-11-08). Scoop's .NET SDK installs have overwritten it
  (twice); the machine notes say how to check and restore it (the workspace's
  `cmem/local/environment.md`).
- 🔒 **The .NET versions stay separated by COMMAND, and `MSBuildSDKsPath` stays unset** (owner,
  2026-11-08: "lets make sure that the versions stay separated to avoid issues that clash
  between versions", then "yes" to removing it). `dotnet` = the .NET 8 programs, `dotnet10` =
  SDK 10, `dotnet11` = SDK 11; switching versions means typing the other command. The
  `MSBuildSDKsPath` environment variable forces ONE SDK's build logic on every SDK. The .NET 11
  preview install had saved it pointing at SDK 11, and then `dotnet10 build` failed in every new
  terminal (`MSB4216`). Pointing at SDK 10 (an older shell's value), it made `dotnet11` refuse
  `net11.0` (`NETSDK1045`). Removed from the user environment 2026-11-08; with it unset, the
  build, 59/59 tests, the Release build and the scripts pass on `dotnet10`. Never set it for this
  repo's work. After a scoop .NET install, check it along with `DOTNET_ROOT` (machine notes).
  A shell started before the change keeps the old values (VS Code's terminals did): restart it.
  **A check of "a new terminal" must rebuild the WHOLE environment from the saved values**, not
  only `PATH` / `DOTNET_ROOT`. The 2026-11-08 check that missed this reset only those two.
- **Letter H15 (workspace `handoffs.md`) — closed by the workspace 2026-10-22** on this repo's
  earlier record ("nothing to port", checked at `f81cb55` = `9bb0742` after the history rewrite;
  `git grep -i -E 'deno|\bbun\b|\bnode\b|npx'` matched nothing; the word boundaries matter: without
  them `bun` matches `ubuntu-latest`).

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
- **`dotnet tool install` / `dotnet new tool-manifest` write `dotnet-tools.json` with CRLF**
  (seen 2026-11-08): rewrite it as LF before committing.
- **Status 2026-10-22:** the index was already all LF; `.gitignore` and `LICENSE` were CRLF on disk
  and were converted.

## No secrets, no personal information (owner, 2026-10-22)

- 🔒 Nothing committed may hold a secret or personal information: secrets by NAME only, never a
  value; no personal emails or machine facts (those go in the workspace's
  `cmem/local/environment.md`). Applies to files, commit messages, the package and issues.
- 🔒 **The path test is "machine-specific?"** (owner, 2026-10-23, workspace letter H22): a path
  that records the layout of one of the owner's machines is personal information —
  `D:\Programs\…`, `C:\Users\…`, scoop / AppData / Temp paths, home-relative ones included, not
  only user-named paths (H18). A convention path that names nothing about one machine (e.g.
  `%USERPROFILE%\.nuget\packages`, `C:\zig-cache\wazmrt`) is fine. Use repo-relative paths or
  placeholders (`<repo-root>`).
- 🔒 **One maintainer contact address is allowed, in three places only** (owner, 2026-10-23/26,
  letter H28): the manifest field a package registry reads, the git author/committer address, and
  ONE committed `cmem/` note naming the maintainer contact. Here: commits use the GitHub noreply
  address (this repo's local `user.email`); `UniversalWasmLoader.csproj` has `<Authors>` and no
  email field; there is no contact note. Nowhere else.
- **Scan text AND binaries** (`-a`, not `-I`: wasmtk and binaryang found paths in committed
  `.wasm` debug info that every `-I` scan skipped, 2026-10-26): before every commit,
  `git grep -n -a -i -E '[A-Za-z]:[\\/](Programs|Users)|AppData|scoop|\\Temp\\|@gmail'` (and before
  pushing, `git grep … origin/main`). Hits at 2026-10-26, all reviewed and none a path: this
  rule's own text and the purge record's prose in [overview.md](overview.md) (the word "scoop").
- The 2026-10-22 history rewrites are in [overview.md](overview.md) → "Build / release flow".
