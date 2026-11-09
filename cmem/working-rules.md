# Working rules — universalWasmLoader-dotnet

Repo-specific rules. The workspace floor (`../../cmem/working-rules.md`) still applies underneath.

## Committed scripts are Bun TypeScript (owner, 2026-11-09)

- 🔒 **The rule (owner, 2026-11-09: "move the other scripts to bun", after "use bun instead of
  dotnet script to release"):** committed scripts are `scripts/*.ts`, run by **Bun**:
  `bun scripts/<name>.ts [args]`. Today: `fixtures.ts`, `runtimes.ts`, `compat.ts` (the gate),
  `github-release.ts` (the release page). They import only `node:*` and use `Bun.*` (e.g.
  `Bun.YAML`, `Bun.which`, `Bun.spawnSync`); no package dependencies, no `package.json`. In CI,
  Bun is **installed by hand, pinned by version + sha256** (`BUN_VERSION` / `BUN_SHA256` in both
  workflows' `env`, move them together; no `setup-bun` action). The version is the latest
  installed Bun (workspace Tooling rule 10, letter H31): 1.4.4, whose `bun-linux-x64.zip` sha256
  `bf8ccbfb…395c` was re-derived 2026-11-09 from Bun's `SHASUMS256.txt` and by hashing the zip.
  Workspace Tooling rule 1: C# repos use Bun (the table's C# row, as before 2026-11-08).
- **Why (2026-11-09):** one runtime instead of .NET + dotnet-script + Bun; no ~13 s first-run
  compile and restore; no dotnet-script `D:`-directory pitfall (below); `Bun.YAML` replaces the
  YamlDotNet package; and no third-party package code beside the release job's write token (H32's
  recipe, C30), the reason the release script moved first.
- **The port back (2026-11-09), under the workspace port rule** (a port replaces its original
  only after an output diff proves them equal). `fixtures.ts` and `compat.ts` were restored from
  `703e452^`, the Bun originals; their `.csx` were never changed after the 2026-11-08 port that
  proved them equal. `runtimes.ts` is that original plus the two later `.csx` changes (`b3941b2`:
  every `<TargetFrameworks>` element, "ahead of its GA" notes; `eb6772d`: the 30 s fetch limit).
  The `.ts` and `.csx` then ran on the same inputs (one-time harnesses, deleted). fixtures on
  `tests/`, an explicit dir and a broken copy; runtimes selftest (27/27) and the live check; compat
  selftest, `surface --check` and `report`: all identical. Two throwaway clones (one per version)
  ran nine gate cases: a dirty-tree refusal, an untagged release, an allowed `bump patch`,
  release, a stale surface ×2, red previous tests ×2, an unknown command. Every verdict, exit
  code and `git diff` was identical; the only differing text was the random temp-folder name and
  xUnit's elapsed time in the printed test tail, each line checked. The usage lines name their
  own file. The `runtimes.ts` fetch limit was inverted: an unroutable address with a 3 s limit
  fails after 3 s, naming both. The release page script's own port proof is in
  [overview.md](overview.md) → "Build / release flow".
- **History:** 2026-10-22 "language first" (C# file-based apps) → "committed scripts only" →
  "no mechanism that builds an executable" (C# → Bun): the gate was written as
  `scripts/compat.cs` (`51c447f`, never pushed), then ported to `compat.ts` (`0874746`). On
  2026-11-08 the scripts moved to `.csx` via dotnet-script (owner: "move them", `703e452`),
  pinned as a local tool. **dotnet-script facts, kept in case it is ever reconsidered:**
  - It builds no `.exe`: one cached `.dll`, run in the tool's process.
  - Never the bare shim `dotnet-script <file>`: the owner saw it throw errors.
  - From a `D:` directory, the GLOBAL `dotnet10 script` also runs the bare shim. Its internal
    `dotnet restore` then finds the SDK-less system `dotnet` and fails, the likely cause of the
    2026-10-22 failure.
  - A repo-local tool always runs inside the SDK.

  On 2026-11-09 the release script moved to Bun (`75f8dd1`), then the rest (`e047771`), and
  `dotnet-tools.json` was removed. Workspace P6 in `../../cmem/divergences.md`.
- **Scope:** the rule governs COMMITTED scripts kept for repeated use (what would otherwise be a
  `.ps1`/`.sh`/`.cmd`). Product code and the test suite are not scripts; project tasks stay plain
  `dotnet10 build` / `dotnet10 test tests` / `dotnet10 pack`. **A one-time script deleted after use
  may be PowerShell, bash, Bun or anything else that fits**, in the session scratchpad, still
  WRITTEN with `Write` (never a heredoc); ported to Bun before it is committed.
- **No committed shell scripts.** CI YAML `run:` steps only launch commands (the pinned Bun
  install is the one multi-line step, the same in every job that needs Bun).
- 🔒 **No `gh`, in CI too** (workspace Tooling rule 9, letter H30; owner, 2026-11-08: "it has been
  found to be more complicated to use than it is worth", "yes change those"). Check the repo
  instead: `git ls-remote origin`, `git fetch`, the repo's files, a registry's public API
  (nuget.org's flat-container), GitHub's public REST API for public data. A task only GitHub's
  web UI can do (a Release page by hand, run logs, a Support request) is an OWNER item. Never
  write "blocked on gh". In workflows, GitHub's REST API with the job's `GITHUB_TOKEN`: the
  `release` job runs `scripts/github-release.ts` ([overview.md](overview.md) → "Build / release
  flow").
- 🔒 **Every release gets its GitHub Release page made BY CI** (letter H32, owner 2026-11-09:
  "the release page should be referred back to the workspace to insure that all projects have
  this setup"; "only new tags will get release pages", so no backfill). Never by hand, never the
  GitHub CLI. Here: `publish.yml`'s `release` job, `contents: write` only, after the nuget.org
  push, running `scripts/github-release.ts` in H32's order: (1) the tag peels to the released
  commit; (2) an existing page = done, no write; (3) POST with the nuget.org link on top + attach
  + read back + peel again; a 30 s limit per request. That job checks out only the script
  (sparse, no credentials) and installs only pinned Bun: no third-party package code beside the
  write token. A change to it lands under the gate before a release, never during one.
- **Bun versions (workspace Tooling rule 10, letter H31):** every Bun notation here is the latest
  installed Bun (1.4.4 on 2026-11-09). Move `BUN_VERSION` and `BUN_SHA256` together in both
  workflows, re-deriving the sha256 from Bun's `SHASUMS256.txt` and by hashing the zip. A dated
  measurement that names a Bun version stays as it was measured.
- **`scripts/` needs no csproj exclusion:** the SDK compiles only `.cs`.
- **Use `dotnet10`, never bare `dotnet`** (owner, 2026-10-22; reason restated by the owner
  2026-11-08): on the development machine bare `dotnet` is .NET 8, kept because installed
  programs depend on that version, and `dotnet10` is .NET 10. This repo is .NET 10 only from
  0.2.0. Never change what bare `dotnet` points at. See [overview.md](overview.md) → "Toolchain".
  `compat.ts` picks `$UWL_DOTNET`, else `dotnet10` when on PATH, else `dotnet` (CI).
- **`dotnet11` is the .NET 11 SDK** on the development machine (added 2026-11-08 at the owner's
  request, the same kind of command as `dotnet10`; 11.0.100-rc.3 that day). Use it only for
  .NET 11 work (the `net11.0` test target at GA: [open-work.md](open-work.md)); the repo's
  commands stay on `dotnet10`.
- 🔒 **`DOTNET_ROOT` stays the .NET 8 system install** (owner, 2026-11-08: "I have to keep the
  dotnet root as dotnet 8 for installed programs that are not yet compatible with dotnet 10").
  Never change it, and never set it for this repo's work. `dotnet10` / `dotnet11` find their own
  SDKs without it. With it on the .NET 8 install, this repo's build, 59/59 tests and the
  scripts all pass (2026-11-08). Scoop's .NET SDK installs have overwritten it
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
