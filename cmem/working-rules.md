# Working rules — universalWasmLoader-dotnet

Repo-specific rules. The workspace floor (`../../cmem/working-rules.md`) still applies underneath.

## Committed scripts are TypeScript run by Bun (owner, final 2026-10-22)

- 🔒 **Workspace Tooling rule 1, as it stands 2026-10-22:** a committed script uses the repo
  language's own scripting mechanism, **unless that mechanism has to BUILD AN EXECUTABLE**; then,
  and where there is none, TypeScript run by Bun. **C# → Bun** (the workspace table's row):
  `dotnet10 run --file` writes `.exe` + `.dll` (measured by the workspace). So this repo's scripts
  are `scripts/*.ts`, run as `bun scripts/<name>.ts` (Bun 1.4.3 locally and in CI).
- **The rule changed three times on 2026-10-22; this repo followed each:** "language first" (C#
  file-based apps) → "committed scripts only" → "no mechanism that builds an executable" (C# →
  Bun). The compatibility gate was first written as `scripts/compat.cs` (local commit `51c447f`,
  never pushed) and ported to `scripts/compat.ts` in `0874746` before anything was published; no
  proof-of-equivalence was owed, since no C# version was ever in use. History: P6 in
  `../../cmem/divergences.md`.
- **`dotnet-script` (`.csx`) was tried 2026-10-22 and does not run here** (owner installed 2.0.1):
  it restores with `-r win10-x64`, which the .NET 10 SDK rejects (`NETSDK1083`; .NET 8+ dropped
  version-specific Windows RIDs), and it mirrors the script's full path under
  `%TEMP%\dotnet-script\`, which overflowed MAX_PATH for a scratchpad script. It never reached the
  compile step, so what it writes was NOT measured (by design a cached `.dll`, no `.exe`). Owner's
  call the same day: Bun now; port later under the proof rule if C# gains a listed mechanism.
- 🔒 **Re-asked 2026-11-08; the owner's decision: KEEP BUN FOR NOW** ("1"; "the scripting is
  still not working yet", i.e. C# scripting does not work on this machine yet, so revisit when it
  does). Measured that day on SDK
  10.0.403 with throwaway scripts (deleted): `dotnet10 run --file x.cs` still builds `x.exe` (an
  apphost) + `x.dll` under `%TEMP%\dotnet\runfile\`, and runs AS that `.exe` (8.8 s first run,
  0.2 s cached). With `#:property UseAppHost=false` it builds no `.exe` and runs as a cached
  `.dll` under the SDK's `dotnet.exe`, the `dart run` / `.main.kts` shape. That was offered as a
  possible fit for the rule and NOT taken. `dotnet10 repl` is not an SDK command; it would be the
  separately installed `dotnet-repl` tool. `.csx` needs `dotnet-script` or Roslyn `csi`, neither
  in the SDK. Neither is installed here: only a shim-less `dotnet-script` 2.0.1 leftover in
  `~\.dotnet\tools\.store` from the 2026-10-22 trial. An installed tool would be a CI dependency
  like Bun, so it gains nothing. To revisit once C# scripting works here: the `UseAppHost=false`
  route, porting `fixtures.ts` first with an output diff (workspace port rule).
- ✅ **`dotnet-script` RUNS here since 2026-11-08** (supersedes the 2026-10-22 bullet above and
  the "neither is installed" clause just above). The owner's correction: the command is
  `dotnet10 dotnet-script <file>`; `dotnet10 script <file>` and plain `dotnet-script <file>` work
  too (the owner confirmed `dotnet10 script`). Measured with a throwaway `.csx` (deleted):
  `dotnet-script` 2.0.1 is a `dotnet10` global tool (shim `~\.dotnet\tools\dotnet-script.exe`,
  which my check earlier the same day found absent). It runs on the .NET 10.0.14 runtime, with
  args after `--`; 13.1 s first run (restore + compile), 0.9 s cached. It **builds no `.exe`**:
  its `%TEMP%\dotnet-script\…` cache (the script's full path mirrored, 218 chars for a scratchpad
  script) held one compiled `.dll` + restore files, and the script runs inside the tool's
  process. `NETSDK1083` and the MAX_PATH overflow did not recur. So it fits Tooling rule 1 ("no
  mechanism that builds an executable"). **Not adopted yet:** switching needs the owner's word
  (the workspace table says "C# → Bun"), a CI install step (`dotnet tool install -g
  dotnet-script --version 2.0.1`: third-party NuGet code in the token-less jobs, as `setup-bun`
  is now), and the port rule's output diff per script (`fixtures.ts`, `runtimes.ts`, which
  needs a YAML reader, `compat.ts`).
- **Scope:** the rule governs COMMITTED scripts kept for repeated use (what would otherwise be a
  `.ps1`/`.sh`/`.cmd`). Product code and the test suite are not scripts; project tasks stay plain
  `dotnet10 build` / `dotnet10 test tests` / `dotnet10 pack`. **A one-time script deleted after use
  may be PowerShell, bash, Bun or anything else that fits**, in the session scratchpad, still
  WRITTEN with `Write` (never a heredoc); promoted to Bun before it is committed.
- **No committed shell scripts.** CI YAML `run:` steps only launch commands.
- **Bun scripts use `node:*`, web APIs and `Bun.*`** (workspace rule 1), never `Deno.*`. `scripts/`
  needs no csproj exclusion: the SDK compiles only `.cs`.
- **Use `dotnet10`, never bare `dotnet`** (owner, 2026-10-22; reason restated by the owner
  2026-11-08): on the development machine bare `dotnet` is .NET 8, kept because installed
  programs depend on that version, and `dotnet10` is .NET 10. This repo is .NET 10 only from
  0.2.0. Never change what bare `dotnet` points at. See [overview.md](overview.md) → "Toolchain". `compat.ts` picks `$UWL_DOTNET`,
  else `dotnet10` when on PATH, else `dotnet` (CI).
- **Letter H15 (workspace `handoffs.md`) — closed by the workspace 2026-10-22** on this repo's
  earlier record ("nothing to port", checked at `f81cb55` = `9bb0742` after the history rewrite;
  `git grep -i -E 'deno|\bbun\b|\bnode\b|npx'` matched nothing; the word boundaries matter: without
  them `bun` matches `ubuntu-latest`). The repo has since gained its first committed script,
  `scripts/compat.ts`, which conforms.

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
