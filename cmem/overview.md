# Overview — universalWasmLoader-dotnet

## What this is

The **.NET / C#** port of the Universal WASM Loader. It loads a `.wasm` library (the reactor/library
shape `wasmtk modc` produces), reads its WIT-described interface, and exposes each export as a
type-safe .NET method with Canonical-ABI marshalling handled for the caller (numbers direct, strings
encoded/decoded through linear memory, booleans normalized). No `.wit` → raw exports. Sibling of the
reference JS/TS loader and the Go/Rust/Python ports; conforms to the cross-language `SPEC.md`.

- **Language / runtime:** C# on .NET, built on **Wasmtime** (NuGet `Wasmtime`, the Bytecode Alliance
  wasmtime-dotnet binding). **Namespace:** `UniversalWasmLoader`; **PackageId:** `UniversalWasmLoader`.
- **Distribution:** **NuGet** (`dotnet add package UniversalWasmLoader`). **Published — v0.1.0 is live
  on nuget.org (2026-06-24).**

## Repository home — `synapticforge1` organization (recorded 2026-10-20)

- **Canonical home:** **https://github.com/synapticforge1/universalWasmLoader-dotnet**. The owner
  moved it from the personal `jrmarcum` account to the `synapticforge1` org, which the owner controls.
  Checked 2026-10-20: `git ls-remote` on the new URL returns HEAD `04b1b6b` (now `d990087`, see the
  2026-10-22 history rewrite below), the same as the local `main`. The reference loader moved too: **`synapticforge1/universalWasmLoader-js`**. The -c and -dart
  ports are already there as well.
- **URLs repointed 2026-10-20:** the local `origin` remote now uses the org URL. Commit `f652704`
  (now `de0faea`; pushed) updates the `UniversalWasmLoader.csproj` `<RepositoryUrl>` and the README
  link to the JS loader. The `RepositoryUrl` goes into the NuGet package metadata, so nuget.org shows
  the new URL only from the next published version.
- **NuGet Trusted Publishing after the transfer:** the owner reports (2026-10-20) that nuget.org
  updated the policy's Owner to `synapticforge1` on its own, so it needed no manual change.
  **Verified by the owner 2026-10-22:** the `NUGET_USER` repo secret survived the transfer, and the
  org's Actions policy allows `NuGet/login@v1` and `actions/*`.

## Runtime decision — Wasmtime (confirmed 2026-06-17)

**Wasmtime NuGet** (the intended/expected dep, now confirmed in `UniversalWasmLoader.csproj`:
`<PackageReference Include="Wasmtime" Version="44.0.0" />`). Unlike Go — where a head-to-head
benchmark flipped the choice from wasmtime-go to **pure-Go wazero** (~38× lower call overhead, no
CGO) — **.NET has no production-grade pure-managed wasm runtime equivalent to wazero**, so the
P/Invoke-based Wasmtime binding stands as the right choice (and gives WASI P1 for free, SPEC §10).
Spiked here first (Wasmtime 44 restores + runs on win-x64; `add(3,4)=7`, env imports, full string ABI)
before writing the port.

**Toolchain:** needs the **.NET 10 SDK**. **Run it as `dotnet10`, never bare `dotnet`** (owner,
2026-10-22; workspace Tooling rule 1): on the development machine bare `dotnet` resolves to a
runtime-only install with no SDK. Where `dotnet10` comes from is a machine fact, kept out of this
committed file: the workspace's `cmem/local/environment.md`. Checked 2026-10-22: `dotnet10 --version`
→ 10.0.402. Build, test and pack go through it; committed scripts are Bun (`bun scripts/<name>.ts`,
[working-rules.md](working-rules.md)). The test project
targets **net10.0**; the library multi-targets **net8.0;net10.0** for consumers.

## Current state — IMPLEMENTED + SPEC-3.0.0 conformant (2026-06-17)

Fully implemented and passing the reference suite (was a pre-implementation stub). Source (namespace
`UniversalWasmLoader`):

- `Wit.cs` — WIT parser (mirrors the JS `wit-parser.js`; `GeneratedRegex`, kebab→camel, s32 fallback).
- `WasmModule.cs` — the `WasmModule` handle: `Call` / `Bind`, Canonical-ABI param encode (incl. string
  via `cabi_realloc`) + result decode (incl. **callee-allocated string returns** — read the `[ptr,len]`
  pair, decode, then call `cabi_post_<camel>`), `Dispose`. Args marshalled to `Wasmtime.ValueBox`.
- `Loader.cs` — `Load` (sync, file) / `ImportAsync` (file or `http(s)` URL) / `CreateSingleton`; core
  instantiation; **dynamic `env` host-import functions** (`Function.FromCallback(store, untyped, paramKinds,
  resultKinds)` → decode `ValueBox` per WIT, `DynamicInvoke` the user delegate, encode result);
  `@N` version pin; WASI-P1 (`linker.DefineWasi()` + inherited stdio) + `_initialize` (SPEC §10).
- `Callbacks.cs` — `new Callbacks().On(camelName, Delegate)`.
- `InstancePool.cs` — `BlockingCollection`-backed pool of N independent instances (`Acquire`/`Release`/
  `Run`/`Size`/`Available`/`Dispose`).

### Public API

`Loader.Load(path, cbs?)` · `Loader.ImportAsync(src, cbs?)` · `Loader.CreateSingleton(path, cbs?)` ·
`WasmModule.Call(name, params object?[])` → `object?` · `WasmModule.Bind(name)` · `WasmModule.Exports`
· `WasmModule.Dispose()` · `new InstancePool(path, size, cbs?)` · `new Callbacks().On(name, Delegate)`.
Exports are looked up by **camelCase** name (SPEC name-form rule). The source-generator typed
interface (PORT_PROMPT item 4) is NOT implemented — `Bind` covers the destructure pattern; codegen is
a future add (not required for SPEC §7 conformance).

## Canonical ABI / SPEC conformance

- Built **directly against SPEC v3.0.0** (callee-allocated string returns + `cabi_post_<name>`); the
  deprecated 2.x out-param convention was skipped. Component ABI only (matches `abi.js`; numeric/bool
  handling is profile-independent; strings use `cabi_realloc`/`cabi_post`).
- Wasmtime's `Function.Invoke` returns a boxed `int` for i32 results, so bool decode is `(int) != 0`
  (no high-bit-garbage issue — that bit Go/wazero needed `uint32` masking for does not arise here).

## Tests

- **Command:** `dotnet10 test tests` — **9/9 pass** (2026-10-22, SDK 10.0.402) (`tests/LoaderTests.cs`,
  xunit). Covers SPEC §8 (`math_50`/`booleans_50`/`strings_50`/`imports_50`, copied into `tests/` and
  copied to output) + `Bind`, singleton identity, and a concurrent `InstancePool` (size 2, `Parallel.For`
  over 8), and **SPEC §10** (added 2026-10-22, ported from -js `loader.test.js:95-111`):
  `Initialize` (`init_check.wasm`, raw-exports path: `getValue` = 99 proves `_initialize` ran) and
  `WasiIo` (`wasi_io_50.wasm` imports `fd_write`: `logAndDouble(21)` = 42 proves the WASI shim).
  **Inverted** with loader mutants (forced rebuilds): `_initialize` call removed → `Initialize`
  fails; `DefineWasi()` removed → `WasiIo` fails ("unknown import:
  `wasi_snapshot_preview1::fd_write` has not been defined"); restored, 9/9.
- **Fixtures = the stamped -js reference set (H7 / L3, 2026-10-22).** Copied from
  `universalWasmLoader-js` commit `bd4f8ab` (pushed): `tests/*.wasm`, `*.wit`, `init_check.wat` and
  the stamp `tests/fixtures.json` (generator wasmtk `f50c8e4`). All 12 sha256 verified against the
  stamp; 7/7 pass, and the test output dir was checked to hold the stamped bytes (`Copy-Item` keeps
  the source's older mtime, and the test csproj copies with `PreserveNewest`, so check this after any
  re-copy). Before: the four `.wasm` differed from the stamp, the four `.wit` matched; old set backed
  up only in that session's scratchpad.
  - **Re-stamped 2026-10-26 (H7 / C11 / drift X8):** wasmtk purged its history twice, so the
    generator `f50c8e4` (2026-10-22: on wasmtk's local `main` only, never on GitHub) became
    `84b3e180eba`, then `4a5c3cf668d`, which is on wasmtk `origin/main`. loader-js re-stamped from it
    in `36aa661` (on its `origin/main`) with every fixture byte unchanged; this repo COPIED that
    `tests/fixtures.json` verbatim — `generator.repository` / `commit` / `describe` now
    `synapticforge1/wasmtk`, `4a5c3cf668da…`, `v2.0.3-64-g4a5c3cf`. Copied, not regenerated:
    `describe`'s abbreviation length depends on the clone (loader-dart's finding, C15). All 12
    fixture files byte-identical to -js (`cmp`), `bun scripts/fixtures.ts` 12/12, `dotnet10 test
    tests` 9/9.
  - **Hash check: `bun scripts/fixtures.ts`** (2026-10-22; same rule as -js
    `scripts/check-fixtures.ts`): every stamped file present and sha256-identical, no unstamped
    `.wasm`/`.wit`/`.wat` in `tests/`. Runs in CI `validate.yml`. Inverted on a copy (one flipped
    byte, a stray `.wit`, a missing `.wat` → exit 1 naming all three); `tests/` passes, 12 files.
- **`tests/` is tracked (2026-10-22, with the compatibility gate):** every tag must carry the suite
  the next release has to pass ([compat-gate.md](compat-gate.md) Rule 3). Audited first: no personal
  data, LF throughout, nothing embedded in the `.wasm`. CI (`validate.yml`) runs `dotnet test tests`
  on every push.
- **Project layout gotcha:** the library csproj's implicit `**/*.cs` glob swept up `tests/*.cs`; fixed
  with `<Compile Remove="tests/**/*.cs"/>` (+ Content/None) in `UniversalWasmLoader.csproj`.

## Build / release flow

- **Build:** `dotnet10 build -c Release`. **Test:** `dotnet10 test tests` (locally; CI's `dotnet` is
  the runner's own SDK).
- **Pack:** `dotnet pack -c Release -o ./out` → produces `UniversalWasmLoader.<ver>.nupkg` **and**
  `.snupkg` (symbols enabled via `<IncludeSymbols>`/`<SymbolPackageFormat>snupkg</SymbolPackageFormat>`).
- **Release (current, keyless):** publishing is via **NuGet Trusted Publishing (OIDC)** — no API key
  stored. Workflow `.github/workflows/publish.yml` runs on a published GitHub Release (or manual
  `workflow_dispatch`), uses `NuGet/login@v1` (`permissions: id-token: write`) to exchange a GitHub
  OIDC token for a 1-hour nuget.org key, then `dotnet nuget push "out/*.nupkg" --skip-duplicate`
  (the `.snupkg` rides along). Manual prerequisites done once: nuget.org Trusted Publishing policy
  (Owner `synapticforge1` since the 2026-10-20 move, which nuget.org updated itself; originally
  `jrmarcum`. Repo `universalWasmLoader-dotnet`, Workflow `publish.yml`, no environment) and the
  `NUGET_USER` GitHub repo secret (nuget.org profile name, not email).
- **First publish:** **v0.1.0 published to nuget.org 2026-06-24**, from commit `d990087` (the
  nuspec's `repository commit` is `04b1b6b`, the pre-rewrite hash of the same commit), by a manual
  `workflow_dispatch` run of `publish.yml`. Tagged `v0.1.0` → `d990087` and pushed 2026-10-22.
- **0.1.1 is bumped, not released** (`c4f5e84`, 2026-10-22, through the gate at floor patch). Since
  0.1.0 it carries no API change: the README Versioning section and test-command fix, the
  PublicAPI analyzer (build-time only), tracked `tests/`, CI `validate`, and the new `RepositoryUrl`.
  **Tagged `v0.1.1` → `7451a46` and pushed 2026-10-22** (owner: "all of them"), after `bun
  scripts/compat.ts release` passed locally on the tagged HEAD (floor patch). **The GitHub Release
  is NOT published yet:** `gh` is not authenticated on this machine (no `GH_TOKEN`), so the owner
  publishes it (web UI: Releases → Draft a new release → tag `v0.1.1` → Publish). Publishing runs
  `publish.yml`: tests, self-test, the release gate, then pack + push 0.1.1 to nuget.org.
- ✅ **0.1.1 PUBLISHED to nuget.org 2026-10-22** — run 39821337557 attempt 3 (re-run after the
  owner fixed the trusted-publishing policy on nuget.org; details of the fix are the owner's):
  every step `success`, including the release gate, NuGet login and push. The 0.1.1 nuspec names
  `https://github.com/synapticforge1/universalWasmLoader-dotnet` at commit `8978aaf` — the tag's
  hash at publish time; the second purge (below) made it `7451a46`, same tree. So the old-URL item
  is superseded from 0.1.1 on, but both published packages now name a commit the repo no longer
  has (0.1.0: `jrmarcum` / `04b1b6b`; 0.1.1: `8978aaf`); nuget.org metadata is immutable. The flat-container
  listed 0.1.0, 0.1.1 right away; the registration index still showed only 0.1.0 a few minutes
  later (indexing lag, not re-checked). **Note:** GitHub's "Re-run" keeps the run id and bumps
  `run_attempt`; a watcher must not filter re-runs out by id.
- **History of the 0.1.1 publish (superseded by the line above):** the first attempt FAILED at
  `NuGet/login@v1` (owner ran `publish.yml` by
  `workflow_dispatch` on `v0.1.1`, run 39821337557, 2026-10-22): "Token exchange failed (HTTP 401) …
  No matching trust policy owned by user '***' was found. Make sure you are using the username of the
  policy creator, not the policy owner." Every earlier step passed (tests, self-test, **release
  gate**, restore, build, pack); NuGet push was skipped; nuget.org still lists only 0.1.0. Likely
  cause: the repo transfer — the OIDC token now says repository owner `synapticforge1`, and the
  policy's GitHub repository-owner field (or `NUGET_USER`) no longer matches. **This contradicts the
  2026-10-20 note above that the policy "needed no manual change"**; that note was about the
  policy's nuget.org owner, not its GitHub fields.
  **Most likely cause, from NuGet's docs** (learn.microsoft.com/nuget/nuget-org/trusted-publishing,
  § "Policies Pending Full Activation"): "NuGet needs GitHub repository and owner IDs to lock the
  policy to the original repo and owner … Once a successful publish provides the IDs …, the policy
  becomes permanently active." v0.1.0's successful publish (2026-06-25) locked the policy to owner ID
  **81428316** (user `jrmarcum`); the repo now belongs to org `synapticforge1`, owner ID
  **342141589** (repository ID 1243565389; checked via the GitHub API 2026-10-22). Renaming the
  policy's owner field cannot change the locked ID. **Fix: a NEW policy** (Repository Owner
  `synapticforge1`, Repository `universalWasmLoader-dotnet`, Workflow File `publish.yml`, no
  environment), `NUGET_USER` = the nuget.org username of whoever CREATES it, delete the old one,
  re-run `publish.yml` on `v0.1.1` (safe: `--skip-duplicate`). A second paste of the same error
  (2026-10-22) matched no new run: still run 39821337557. **Applies to every repo moved to the org
  that publishes through a trusted-publishing policy** (cf. -js, -c, -dart registries).
- **Releasing now goes through the compatibility gate** ([compat-gate.md](compat-gate.md), built
  2026-10-22): `bun scripts/compat.ts bump <patch|minor|major>` sets `<Version>` (refusing below the
  SemVer floor), then commit, tag `v<ver>`, publish a GitHub Release; `publish.yml` re-runs the gate
  on the tag (blocking) before packing. nuget.org rejects re-pushing an existing version.
- **Licensing:** package license is **MIT** (`<PackageLicenseExpression>MIT</PackageLicenseExpression>`),
  corrected from an earlier `MIT OR Apache-2.0` that had no matching Apache file; README §License synced.
- **Repo hygiene (2026-06-24):** `CLAUDE.md`, `cmem/`, and `tests/` were made **gitignored and
  untracked** (local-only); `out/` is gitignored alongside `artifacts/`. These paths still exist in
  earlier git history (not purged).
- **`cmem/` is tracked again (owner, 2026-10-22; H7 / L1)**, on one condition: 🔒 **no secrets and
  no personal information are ever made public**. Audited before un-ignoring: one personal item
  removed (a Windows user-profile path to the SDK) and the machine facts moved to a pointer to the
  workspace's `cmem/local/environment.md`. Secrets appear by NAME only (`NUGET_USER`), never by
  value. `CLAUDE.md` stays gitignored; `tests/` was tracked later the same day (see "Tests").
- **`.claude/settings.local.json` untracked + gitignored (2026-10-22).** It is per-machine Claude Code
  settings and was tracked by mistake; a permission entry in it held a local user-profile path.
- **History rewritten to remove personal information (owner: "necessary go", 2026-10-22).** Found by
  `git grep` over every commit: a Windows user-profile path in the 2026-06-17 `cmem/overview.md:27`
  and in the tracked `.claude/settings.local.json`; and the owner's personal email as author/committer
  on every commit. Done with `git filter-branch` on `main` (no tags existed): the settings file removed
  from every commit (its only commit, "update" 2026-10-20, became empty and was dropped: 10 → 9
  commits); the one old `overview.md` blob replaced with a copy whose SDK path no longer names the user; the
  email replaced by the GitHub noreply `81428316+jrmarcum@users.noreply.github.com` (public account
  id), also set as this repo's local `user.email`. Verified: no hits in any tree, message or metadata;
  final tree identical to before. Force-pushed with a lease on the old head; `origin/main` =
  `7b08f43`. Old objects pruned locally; the pre-rewrite history is kept privately as a bundle in
  the workspace's machine-local `cmem/local/backups/` (never committed anywhere). **Every hash changed** — old → new: `1588c76`→`e063d64`,
  `e6c928f`→`46f8f3b`, `c2224f1`→`6e454c7`, `98dcf2f`→`6e21617`, `de56961`→`142dc21`,
  `04b1b6b`→`d990087`, `f652704`→`de0faea`, `1c0daf9`→dropped, `f81cb55`→`9bb0742`,
  `d8f0707`→`7b08f43`. **Not in our control:** GitHub can still serve the old commits by hash until
  its own garbage collection (full purge = a GitHub Support request), and nuget.org's v0.1.0 package
  metadata/SourceLink names the old pre-rewrite commit.
- **Second purge, same day (owner: "yes purge", 2026-10-22), for workspace letter H18's wider
  definition (ANY absolute path on the owner's machines).** A scan of every commit on `main` and
  both tags found real paths in two blobs only: the old `CLAUDE.md`'s safe.directory line (a drive
  path to an old checkout; commits now `6e454c7`, `6e21617`) and the 2026-06-17 `cmem/overview.md`
  (the system-wide `dotnet` install path and a home-relative SDK path; now `6e21617`). Everything
  else the scan matched was not a path (URLs, regex text, the word "scoop", a folder name, generic
  `%TEMP%`, rule text with `…` placeholders). `git filter-branch --index-filter` swapped exactly
  those two blob ids for cleaned copies (`<repo-path>`; "the system-wide install"; "the scoop
  install of `dotnet-sdk`"), `--tag-name-filter cat -- main v0.1.0 v0.1.1`. Verified: 23 → 23
  commits; the trees of `main`, `v0.1.0` and `v0.1.1` byte-identical to before; the two old blobs
  unreachable; authors, dates and messages identical on all 23 (checked with a positive control);
  no real path in any commit. Force-pushed with a lease on each ref (main `de3dd8e`→`8e66eaf`,
  `v0.1.0` → `d990087`, `v0.1.1` → `7451a46`); old objects pruned. **Map (old → new):**
  `e063d64`, `46f8f3b` unchanged; `0698e13`→`6e454c7`, `f1623bd`→`6e21617`, `06a6934`→`142dc21`,
  `f82e25b`→`d990087`, `6d8e7b4`→`de0faea`, `d6c1525`→`9bb0742`, `8fac2db`→`7b08f43`,
  `7f37a8e`→`adfe5cc`, `4d9ac63`→`51c447f`, `e845238`→`0874746`, `6b6e07a`→`bec8fb8`,
  `9b9f29a`→`3d9055c`, `e4c02b3`→`3f6bd29`, `62c8f44`→`f608dc0`, `daa9137`→`c4f5e84`,
  `8978aaf`→`7451a46`, `6421d44`→`0efec13`, `558fd24`→`cfea43c`, `06029d2`→`5f89af4`,
  `710108c`→`de3dd8e`, `1d12745`→`8e66eaf`. All hashes cited in `cmem/` and `scripts/` were
  rewritten to the new ones, except quotes of published nuget.org metadata. A private bundle of
  the pre-purge state is in the workspace's machine-local `cmem/local/backups/`. GitHub keeps
  serving old commits by hash until its own GC (full purge = a GitHub Support request).
- **`CLAUDE.md` cut to a pointer (L2, 2026-10-22).** Its body still described an empty repo ("No
  source code or project files exist yet"), bare `dotnet`, and an outdated local checkout path. Now: the
  handoffs line, pointers into `cmem/`, the triggers, three things that bite, current state. Archive:
  `git show 6e454c7:CLAUDE.md` (last tracked; later additions were only the repo-URL lines, which
  live in "Repository home" above).
- **Repo rules (2026-10-22):** LF line endings enforced by `.gitattributes` + `.editorconfig`; committed
  repeated-use scripts are TypeScript run by Bun (C# file-based apps build an executable, which the
  workspace rule excludes); one-time scratchpad scripts may be any language; no secrets or personal
  information. Details in [working-rules.md](working-rules.md); method rules in
  [best-practices.md](best-practices.md).
