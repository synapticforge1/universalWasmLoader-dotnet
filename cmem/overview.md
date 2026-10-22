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
  Checked 2026-10-20: `git ls-remote` on the new URL returns HEAD `04b1b6b` (now `f82e25b`, see the
  2026-10-22 history rewrite below), the same as the local `main`. The reference loader moved too: **`synapticforge1/universalWasmLoader-js`**. The -c and -dart
  ports are already there as well.
- **URLs repointed 2026-10-20:** the local `origin` remote now uses the org URL. Commit `f652704`
  (now `6d8e7b4`; pushed) updates the `UniversalWasmLoader.csproj` `<RepositoryUrl>` and the README
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

- **Command:** `dotnet10 test tests` — **7/7 pass** (re-run
  2026-10-22, SDK 10.0.402) (`tests/LoaderTests.cs`,
  xunit). Covers SPEC §8 (`math_50`/`booleans_50`/`strings_50`/`imports_50`, copied into `tests/` and
  copied to output) + `Bind`, singleton identity, and a concurrent `InstancePool` (size 2, `Parallel.For`
  over 8).
- **Fixtures = the stamped -js reference set (H7 / L3, 2026-10-22).** Copied from
  `universalWasmLoader-js` commit `bd4f8ab` (pushed): `tests/*.wasm`, `*.wit`, `init_check.wat` and
  the stamp `tests/fixtures.json` (generator wasmtk `f50c8e4`). All 12 sha256 verified against the
  stamp; 7/7 pass, and the test output dir was checked to hold the stamped bytes (`Copy-Item` keeps
  the source's older mtime, and the test csproj copies with `PreserveNewest`, so check this after any
  re-copy). Before: the four `.wasm` differed from the stamp, the four `.wit` matched; old set backed
  up only in that session's scratchpad. **Open:**
  - `wasi_io_50` and `init_check` (SPEC §10, WASI + `_initialize`) are now present but **untested**
    here; add tests.
  - **The generator pin is not public yet:** `f50c8e4` is on wasmtk's local `main` but not on
    GitHub. wasmtk moved to **`synapticforge1/wasmtk`** (owner, 2026-10-22), but re-checked the same
    day with `ls-remote` (read-only): its `main` is still `673d7e3` (2026-09-29), none of its 153
    branches/tags contains `f50c8e4`, and local `main` is 131 commits ahead. It becomes pinnable when
    wasmtk's `main` is pushed (the owner's call, in wasmtk). -js says the ports copy once it is
    pinnable; copied early on owner direction (`tests/` was local-only then; tracked since the
    gate commit, 2026-10-22). The stamp's `generator.repository` still names `jrmarcum/wasmtk`
    (GitHub redirects; -js owns the stamp).
  - No hash-check of the copy runs in this repo (-js has `bun run fixtures`); it would be a second
    Bun script beside `scripts/compat.ts`.
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
- **First publish:** **v0.1.0 published to nuget.org 2026-06-24**, from commit `f82e25b` (the
  nuspec's `repository commit` is `04b1b6b`, the pre-rewrite hash of the same commit), by a manual
  `workflow_dispatch` run of `publish.yml`. Tagged `v0.1.0` → `f82e25b` and pushed 2026-10-22.
- **0.1.1 is bumped, not released** (`daa9137`, 2026-10-22, through the gate at floor patch). Since
  0.1.0 it carries no API change: the README Versioning section and test-command fix, the
  PublicAPI analyzer (build-time only), tracked `tests/`, CI `validate`, and the new `RepositoryUrl`.
  Releasing it = tag `v0.1.1` + publish a GitHub Release (owner's call).
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
  commits); the one old `overview.md` blob replaced with a copy whose path reads `~\scoop\…`; the
  email replaced by the GitHub noreply `81428316+jrmarcum@users.noreply.github.com` (public account
  id), also set as this repo's local `user.email`. Verified: no hits in any tree, message or metadata;
  final tree identical to before. Force-pushed with a lease on the old head; `origin/main` =
  `8fac2db`. Old objects pruned locally. **Every hash changed** — old → new: `1588c76`→`e063d64`,
  `e6c928f`→`46f8f3b`, `c2224f1`→`0698e13`, `98dcf2f`→`f1623bd`, `de56961`→`06a6934`,
  `04b1b6b`→`f82e25b`, `f652704`→`6d8e7b4`, `1c0daf9`→dropped, `f81cb55`→`d6c1525`,
  `d8f0707`→`8fac2db`. **Not in our control:** GitHub can still serve the old commits by hash until
  its own garbage collection (full purge = a GitHub Support request), and nuget.org's v0.1.0 package
  metadata/SourceLink names the old pre-rewrite commit.
- **`CLAUDE.md` cut to a pointer (L2, 2026-10-22).** Its body still described an empty repo ("No
  source code or project files exist yet"), bare `dotnet`, and the old `GithubProjects` path. Now: the
  handoffs line, pointers into `cmem/`, the triggers, three things that bite, current state. Archive:
  `git show 0698e13:CLAUDE.md` (last tracked; later additions were only the repo-URL lines, which
  live in "Repository home" above).
- **Repo rules (2026-10-22):** LF line endings enforced by `.gitattributes` + `.editorconfig`; committed
  repeated-use scripts are TypeScript run by Bun (C# file-based apps build an executable, which the
  workspace rule excludes); one-time scratchpad scripts may be any language; no secrets or personal
  information. Details in [working-rules.md](working-rules.md); method rules in
  [best-practices.md](best-practices.md).
