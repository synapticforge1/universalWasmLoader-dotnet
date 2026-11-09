# Overview — universalWasmLoader-dotnet

## What this is

The **.NET / C#** port of the Universal WASM Loader. It loads a `.wasm` library (the reactor/library
shape `wasmtk modc` produces), reads its WIT-described interface, and exposes each export as a
type-safe .NET method with Canonical-ABI marshalling handled for the caller (numbers direct, strings
encoded/decoded through linear memory, booleans normalized). No `.wit` → raw exports. Sibling of the
reference JS/TS loader and the Go/Rust/Python ports; conforms to the cross-language `SPEC.md`.

- **Language / runtime:** C# on .NET, built on **Wasmtime** (NuGet `Wasmtime`, the Bytecode Alliance
  wasmtime-dotnet binding). **Namespace:** `UniversalWasmLoader`; **PackageId:** `UniversalWasmLoader`.
- **Distribution:** **NuGet** (`dotnet add package UniversalWasmLoader`). **Published: 0.1.0
  (2026-06-24) and 0.1.1 (2026-10-22), both SPEC 3.0.0, net8.0 + net10.0; 0.2.0 (2026-11-08)
  and 0.2.1 (2026-11-09), SPEC 4.0.0, net10.0 only.**

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
2026-10-22; workspace Tooling rule 1): on the development machine bare `dotnet` is .NET 8, kept
for installed programs that depend on that version (owner, 2026-11-08; the earlier note here
said "a runtime-only install with no SDK"), and `dotnet10` is .NET 10. Where `dotnet10` comes from is a machine fact, kept out of this
committed file: the workspace's `cmem/local/environment.md`. Checked 2026-10-22: `dotnet10 --version`
→ 10.0.402; 10.0.403 on 2026-11-07. Since 2026-11-08 there is also **`dotnet11`** (the .NET 11
SDK, 11.0.100-rc.3 that day) for .NET 11 work. **`DOTNET_ROOT` stays the .NET 8 system install**
(owner, 2026-11-08); neither command needs it ([working-rules.md](working-rules.md)). Build, test
and pack go through `dotnet10`; committed scripts are C# scripts run by the
pinned local dotnet-script (`dotnet10 tool run dotnet-script scripts/<name>.csx`, since
2026-11-08; Bun before; [working-rules.md](working-rules.md)). Library and test
project target **net10.0** only, from 0.2.0 (owner, 2026-11-07; 0.1.x was net8.0;net10.0). See
"Tests" for the rule that decides which lines must be tested.

## Current state — 0.2.0 (SPEC 4.0.0, .NET 10 only), 2026-11-07

Re-aligned to SPEC 4.0.0 on 2026-11-07 (workspace row C29): `d5b50c1` (surface re-baseline alone)
and `9e131b2` (behaviour + tests). **0.2.0** (gate floor major; pre-1.0 Rule 6 minor; README
Breaking Changes row): bumped in `93cb185`; .NET 10 only and publish-on-tag-push in `eca6c78`;
**published to nuget.org 2026-11-08** from tag `v0.2.0` = `25a3d10` ("Build / release flow"). Source (namespace `UniversalWasmLoader`):

- `Wit.cs` — the strict WIT parser: a port of loader-js 2.0.0's `wit-parser.js`, with the same
  grammar (SPEC §2 "Parsing") and the same messages, as `FormatException`. An unknown type is an
  error, never `s32`.
- `WasmModule.cs` — the `WasmModule` handle: `Call` / `Bind`. Arguments are checked against the
  WIT signature: count, and the exact .NET type per parameter, with no `Convert.*` (SPEC §4). Then
  Canonical-ABI param encode (string via `cabi_realloc`) and result decode by pattern, including
  **callee-allocated string returns**: read the `[ptr,len]` pair, decode, call `cabi_post_<camel>`.
  WIT exports are resolved to `Function`s at load. `Dispose`.
- `Loader.cs` — `Load` (sync, file) / `ImportAsync` (file or `http(s)` URL, `LoadOptions`) /
  `CreateSingleton`. The **load plan** comes before the `.wasm` is read: the `.wit` is read or
  fetched (only an ABSENT `.wit` is raw), parsed, then string-result imports and missing callbacks
  are refused and each callback delegate's signature is checked against its import. Next comes
  instantiation with the **dynamic `env` host imports** (`Function.FromCallback`: decode `ValueBox`
  per WIT, `DynamicInvoke`, check the returned value's exact type, encode). Every WIT export must
  exist. Also the `@N` version pin and WASI-P1 + `_initialize` (SPEC §10). http(s): one
  `HttpClient` with no timeout of its own, a linked CTS per fetch (`LoadOptions.Timeout`, request
  to last byte), and the error mapping in "SPEC conformance" below.
- `LoadOptions.cs` — `Timeout` (default 30 s; `Timeout.InfiniteTimeSpan` = none; zero, negative or
  over `int.MaxValue` ms refused in `init`) and `CancellationToken`.
- `HostFault` (in `Loader.cs`) carries a host callback's exception across the Wasmtime trap it
  causes, so `Call` rethrows the ORIGINAL exception (with `ExceptionDispatchInfo`). One per store.
- `Callbacks.cs` — `new Callbacks().On(camelName, Delegate)`.
- `InstancePool.cs` — `BlockingCollection`-backed pool of N independent instances (`Acquire`/`Release`/
  `Run`/`Size`/`Available`/`Dispose`).

### Public API

`Loader.Load(path, cbs?)` · `Loader.ImportAsync(src, cbs?, options?)` ·
`Loader.CreateSingleton(path, cbs?)` · `new LoadOptions { Timeout, CancellationToken }` ·
`LoadOptions.DefaultTimeout` · `WasmModule.Call(name, params object?[])` → `object?` ·
`WasmModule.Bind(name)` · `WasmModule.Exports` · `WasmModule.Dispose()` ·
`new InstancePool(path, size, cbs?)` · `new Callbacks().On(name, Delegate)` (27 PublicAPI symbols,
2026-11-07). `Load`, `CreateSingleton` and `InstancePool` are file-only and take no options. SPEC
§11.4 asks them to accept the same options, but the limit applies only to http(s) fetches, so for
them it is n/a, the same reasoning as loader-c's "(5) n/a".
Exports are looked up by **camelCase** name (SPEC name-form rule). The source-generator typed
interface (PORT_PROMPT item 4) is NOT implemented — `Bind` covers the destructure pattern; codegen is
a future add (not required for SPEC §7 conformance).

## Canonical ABI / SPEC conformance

- Built **directly against SPEC v3.0.0** (callee-allocated string returns + `cabi_post_<name>`); the
  deprecated 2.x out-param convention was skipped. Component ABI only (matches `abi.js`; numeric/bool
  handling is profile-independent; strings use `cabi_realloc`/`cabi_post`).
- Wasmtime's `Function.Invoke` returns a boxed `int` for i32 results, so bool decode is `(int) != 0`
  (no high-bit-garbage issue — that bit Go/wazero needed `uint32` masking for does not arise here).
- **SPEC 4.0.0 (2026-11-07, C29): what each refusal throws.** `.wit` unreadable (anything except
  not-found) → `IOException`; HTTP status (other than 2xx, or 404 / 410 on the `.wit`) →
  `HttpRequestException` `wasmImport: HTTP <n> for <url>` with `StatusCode`; network error →
  `HttpRequestException` naming the URL; time limit → `TimeoutException` naming the URL and
  `<n> ms`; cancellation → `OperationCanceledException` carrying the caller's token. A `.wit` that
  does not parse → `FormatException` `invalid .wit '<path>': <fault>`; a WIT export the module
  lacks → `InvalidOperationException`. Missing callbacks (all named), or a delegate whose
  parameters, result or `Task`/`ValueTask` do not fit → `ArgumentException` (param `callbacks`); a
  `string`-result import → `NotSupportedException`; these come before the `.wasm` is read. A
  callback's wrong value → `InvalidOperationException` from `Call`. A typed-call argument →
  `ArgumentException` naming export and parameter.
- **The .NET reading of "never coerced":** an argument or callback value must be EXACTLY the
  mapped type (`int` / `long` / `float` / `double` / `bool` / `string`); `3` is not accepted for
  `f64`, nor an `int` for `s64`. A callback delegate is checked by its static signature at load.
  Only a parameter or result declared `object` is checked by value at call time.
- **No kind-specific result accessors** (`Call` returns `object?`), so C29 (4)'s result-kind half
  does not apply. The result is decoded by pattern; a WASM result of the wrong kind is an error,
  not a conversion.
- **Checked from loader-dart's two defects (C31):** (a) a `.wasm?query` URL: the 3.0.0 code
  appended `.wit` to the whole URL, so it fetched `mod.wasm?v=1.wit`, which is the `.wasm`
  itself. The loose parser then gave an empty world, a typed module with no exports. Fixed (the
  `.wit` URL comes from `GetLeftPart(UriPartial.Path)`) and tested (`Http_QueryIsNotCarriedToTheWit`,
  red on the old code). (b) `s64` on the web: n/a, since Wasmtime is native and `long` crosses as
  i64.

## Tests

- **.NET 10 only from 0.2.0 (owner, 2026-11-07: "I think we need to move to dotnet 10 since the
  EOL for 8 and 9 are this coming week").** Library `<TargetFramework>net10.0`, test project
  `net10.0`, CI installs 10.0.x only (the packed 0.2.0 holds `lib/net10.0` only, `Wasmtime` the
  one dependency). .NET 8 / 9 consumers stay on 0.1.1; README Breaking Changes row item (5).
- **Every required .NET line is tested** (the owner's "like the -js version" question, same day).
  **`scripts/runtimes.csx`** (`runtimes.ts` until 2026-11-08; modelled on -js's `check-lts.ts`) reads
  Microsoft's `releases-index.json`. A line is SUPPORTED when its phase is `active` /
  `maintenance` and its EOL has not passed, and REQUIRED unless it is within 30 days of its EOL
  ("leaving", reported as a note). Each required line must be a test-project target AND installed
  by every CI job that runs `dotnet test` (all `.github/workflows/*.yml`; the push-only `publish`
  job is not checked). It runs in `validate` and in `publish.yml`'s `checks` job, so a release is
  blocked while a required line is untested. So **11.0 (STS, `go-live` RC on 2026-11-07) becomes
  REQUIRED at its GA**: add `net11.0` to the test project and `11.0.x` to both workflows then.
  SDK 10 cannot build `net11.0`; locally use `dotnet11` (installed 2026-11-08). **Already tested
  on the RC since 2026-11-08:** `net11.0` is added when the build runs on SDK 11+, and CI's
  `tests-net11-preview` job runs it; 59/59 on 11.0 rc.3 ([open-work.md](open-work.md) has the
  details and the GA steps). `runtimes.csx -- selftest`: 24
  pure checks. Inverted twice: on the tree before the matrix it failed naming the three gaps;
  `checks` lacking 9.0.x while tests targeted 9 is the per-job case, now a selftest.
- **Before the move, 59/59 also ran on net8.0 and net9.0** (2026-11-08, `4b7b773`, CI run
  42440580340 too). The machine has only the .NET 10 runtime, so the local 8 / 9 runs used
  runtimes from Microsoft's signed `dotnet-install.ps1` in the session scratchpad, found through
  `DOTNET_ROOT` (+ `DOTNET_MULTILEVEL_LOOKUP=0`), then deleted. The same method works for a
  future second line.
- **59/59 pass (2026-11-07, SDK 10.0.403):** the 9 below plus `tests/RefusalTests.cs` (50 cases):
  SPEC 4.0.0 § 8 "Refusals" row by row, with extra cases for each rule (unreadable `.wit`, a path
  without `.wasm`, query dropped, connection refused, cancellation, the timeout's domain,
  delegate-signature refusals, a callback's own exception). The http rows use
  `tests/TestHttpServer.cs`, a `TcpListener` on 127.0.0.1 that can stall and counts requests per
  path, so "one request per file" and "the `.wasm` is never fetched" are asserted from its counts.
  **Inverted:** the same suite against the 3.0.0 behaviour (`d5b50c1`) gave 16 passed / 43 failed,
  exactly the new negatives. Its two stall tests waited **100 s each**, HttpClient's own default,
  the only limit the old code had. **`dotnet10 test` refuses `--no-incremental`** (MSB1001); build
  first (`dotnet10 build tests --no-incremental`), then `dotnet10 test tests --no-build`.
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
  - **Hash check: `scripts/fixtures.csx`** (`fixtures.ts` 2026-10-22 → 2026-11-08; same rule as -js
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
  is NOT published yet:** publishing a Release on GitHub's web UI is an owner item (web UI:
  Releases → Draft a new release → tag `v0.1.1` → Publish). Publishing runs
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
- **A release is a TAG PUSH since 2026-11-07** (owner: "option 2", the loader-dart shape). The
  flow: `dotnet10 tool run dotnet-script scripts/compat.csx -- bump <kind>`, commit, `git tag vX.Y.Z`, push the commit and the
  tag. Pushing the tag runs `publish.yml`, which publishes to nuget.org and then creates the
  GitHub Release itself. Before this, the workflow ran on a published GitHub Release, so pushing
  `v0.2.0` alone published nothing; the Release page was an owner item on the web UI.
- **`publish.yml` jobs (C30, owner's "yes", 2026-11-07; first split `338e003`).** `checks`
  (`contents: read`, no `id-token`): the run must be for a tag at this commit; then fixtures, the
  .NET-line check, tests, self-test, the release gate, restore / build / pack. It uploads
  `out/*.nupkg` + `*.snupkg` as the `nupkg` artifact. `publish` (`id-token: write` only): checks
  the tag is still at `github.sha` with `git ls-remote`, downloads the artifact, sets up the SDK,
  runs `NuGet/login` and `dotnet nuget push`. It has no checkout and runs no package code.
  `release` (`contents: write` only, after a successful push): the same tag check FIRST, then a
  credential-less checkout of the tag, the SDK, `dotnet tool restore` and **`scripts/github-
  release.csx`**: GitHub's REST API with the job's `GITHUB_TOKEN`, .NET base library only. It
  creates the Release with generated notes, or keeps an existing one (a re-run), and uploads only
  the missing `.nupkg` / `.snupkg`. **Since 2026-11-09 (workspace letter H30, owner: no `gh`, in
  CI too)**; until then it was `gh release create <tag> out/* --generate-notes --verify-tag`.
  The REST API creates a missing tag at the default branch where `gh`'s `--verify-tag` refused,
  so the tag check must stay the job's first step. Proven so far by `selftest` (8/8, also in
  `validate`) and dry runs against the real API: `v0.2.0` exists with both assets → nothing to
  do; `v9.9.9` → would create + upload both; one asset missing → only that one; a bad tag or a
  missing token refused. **First real run: 0.2.1 (run 42638787863, 2026-11-09), all three jobs
  green**. The Release `v0.2.1` was made by `github-actions[bot]` with both assets (`.nupkg`
  37 805 B, `.snupkg` 24 804 B, state `uploaded`) and GitHub-generated notes; read back with the
  public API. The re-run path is not exercised (nothing failed).
  **Brought to letter H32's order 2026-11-09 (`3eccba8`, after 0.2.1).** H32 (owner: every
  project's release CI makes its page, only new tags get one) gives an exact order, and the
  script differed from it in three places. Now: (1) the tag peels (annotated followed) to the
  released commit, checked inside the script too (`github-release.csx -- <tag> <sha> <dir>`; the
  workflow passes `github.sha`), else refuse with no write; (2) an existing page = done with NO
  write (it used to upload missing assets to an existing page); (3) POST with
  `generate_release_notes` and the nuget.org link on top, attach the packages, then read it back
  (tag, not a draft, assets present) and peel the tag again. Every request has its own 30 s
  limit, request to last byte. `selftest` runs the whole flow against a fake GitHub API (13
  cases), inverted by three mutants (no sha check, no existing-page stop, no time limit), each
  caught only by its own case(s). Read-only dry runs on the real API: `v0.2.1` at `62386be` →
  exists, nothing to do; at `25a3d10` → refused; `v9.9.9` → refused. Its first real run is the
  next release. Every `uses:` in both workflows
  is pinned to a commit hash (each checked to be a commit through the GitHub API).
  `workflow_dispatch` on a branch fails the tag check by design. No `concurrency:` (C30 (2)).
- **0.2.0 (SPEC 4.0.0, .NET 10 only) on 2026-11-07** (owner: "yes release 0.2.0", then
  "option 2" and "move to dotnet 10"). The tag was first pushed at `93cb185`, under the
  Release-triggered workflow, which started nothing. Checked before moving it: no GitHub Release,
  no 0.2.0 on nuget.org, no `publish.yml` run since 0.1.1. The tag then moved to `25a3d10`
  (the tag-push workflow and the .NET 10 move; `compat.ts release` passed there locally) and was
  force-pushed.
- ✅ **0.2.0 PUBLISHED 2026-11-08** by that tag push: `publish.yml` run 42441589123, all three
  jobs `success`. This was the first run of the tag trigger, of the `release` job, and of the
  hash pins in a credentialed job, so the org's Actions policy accepts `NuGet/login` pinned by
  hash. Read back: nuget.org flat-container lists 0.1.0, 0.1.1, 0.2.0; the 0.2.0 nuspec names
  `net10.0` only, `Wasmtime` 44.0.0, repository commit `25a3d10`. The GitHub Release `v0.2.0` was
  created by the workflow, with the `.nupkg` and `.snupkg` attached.
- ✅ **0.2.1 PUBLISHED 2026-11-09** (owner: "update the project memory files, commit, push then
  release"). `compat.csx -- bump patch` at floor patch (surface unchanged; v0.2.0's tests 59/59
  on the new code; only `<Version>` changed) → release commit `62386be` = tag `v0.2.1` =
  `origin/main` (`ls-remote`); `compat.csx -- release` passed locally on the tagged HEAD, and
  `validate` was green on its parent before the tag push. `publish.yml` run 42638787863: `checks`
  / `publish` / `release` all `success`. Read back: the nuget.org flat-container listed 0.2.1
  about 3½ minutes after the run (the nuspec: 0.2.1, `net10.0`, commit `62386be`); GitHub
  Release `v0.2.1` by `github-actions[bot]`, both assets attached. The library is unchanged since
  0.2.0: this release carried the H30 release path (REST API, no GitHub CLI), and its run is that
  path's first real proof.
- **Releasing goes through the compatibility gate** ([compat-gate.md](compat-gate.md), built
  2026-10-22): `compat.csx -- bump <patch|minor|major>` (Bun `compat.ts` until 2026-11-08) sets `<Version>` (refusing below the
  SemVer floor), then commit, tag `v<ver>`, push both; `publish.yml` re-runs the gate on the tag
  (blocking) before packing. nuget.org rejects re-pushing an existing version.
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
- **Pre-purge commits pruned locally (2026-10-26, H16).** `git gc --prune=now` removed the 7
  commits unreachable since the purges (`0698e13`, `06a6934`, `6d8e7b4`, `8fac2db`, `d6c1525`,
  `f1623bd`, `f82e25b`); each was first confirmed present in the private
  `…-before-rewrite2-2026-10-22.bundle`. So a pre-purge id no longer resolves in this clone: look it
  up in the map above. `maintenance.strategy gc` set; 1 pack / 99.87 KiB; fsck clean after the
  commit-graph repair (best-practices L5).
- **`CLAUDE.md` cut to a pointer (L2, 2026-10-22).** Its body still described an empty repo ("No
  source code or project files exist yet"), bare `dotnet`, and an outdated local checkout path. Now: the
  pull line, pointers into `cmem/`, the triggers, three things that bite, current state. Archive:
  `git show 6e454c7:CLAUDE.md` (last tracked; later additions were only the repo-URL lines, which
  live in "Repository home" above). **Pull line revised 2026-10-26** (owner's "yes"; the ledger's
  rule): read the workspace's `cmem/workspace-correspondence.md` at session start and before each new
  work item, then the letter in `handoffs.md`; answers go in the row's "full text" cell, only the
  row's creator closes it. The pointer had cited the pre-purge `0698e13` for the archive until the
  same day.
- **Repo rules (2026-10-22):** LF line endings enforced by `.gitattributes` + `.editorconfig`; committed
  repeated-use scripts were TypeScript run by Bun, C# `.csx` via dotnet-script since 2026-11-08 (C# file-based apps build an executable, which the
  workspace rule excludes); one-time scratchpad scripts may be any language; no secrets or personal
  information. Details in [working-rules.md](working-rules.md); method rules in
  [best-practices.md](best-practices.md).
