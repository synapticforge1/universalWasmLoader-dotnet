# The compatibility gate (workspace letter H6, built 2026-10-22)

🔒 Binding (owner, 2026-09-20; workspace `../../cmem/layout-and-packaging.md` § "The compatibility
gate"): "backward compatible" is a gate result, never a judgment made at bump time. Modelled on
loader-dart's and loader-c's gates (`../universalWasmLoader-{dart,c}/cmem/compat-gate.md`).

## What runs where

| Rule | Where | Runs in |
| --- | --- | --- |
| 1, the SURFACE | `PublicAPI.Shipped.txt` + `PublicAPI.Unshipped.txt` (committed), enforced by `Microsoft.CodeAnalysis.PublicApiAnalyzers` 5.0.0: RS0016/RS0017/RS0036/RS0037 are build errors | every build; `bun scripts/compat.ts surface --check` in CI `validate` |
| 2, the FLOOR | `scripts/compat.ts` `floorOf` | `bump` (refuses below it), `report`, `release` (CI `publish`, blocking, before pack) |
| 3, BEHAVIOUR | `compat.ts` `previousTests` | same places; required green unless the floor is major |
| 4, deprecate first | README `### ⚠️ Breaking Changes` | a major floor requires a `\| _next release_ \|` row; `bump` stamps the version into it; `release` requires the version's row |
| 5, re-baseline alone | the build/`surface --check` refusal | an intended API change updates the PublicAPI files in its own commit, with the reason |
| 6, pre-1.0 | `problems` | 0.y.z: a minor is allowed where the floor says major; the row is still mandatory |
| self-test | `bun scripts/compat.ts selftest` (59 checks, 2026-10-22) | CI `validate` and `publish`; no git, build or network |

**The surface:** the analyzer's declared API (one line per public symbol, nullable annotations
included, e.g. `static UniversalWasmLoader.Loader.Load(string! path, UniversalWasmLoader.Callbacks?
callbacks = null) -> UniversalWasmLoader.WasmModule!`), as Shipped ∪ Unshipped minus Unshipped's
`*REMOVED*` lines, plus the README's `**SPEC.md vX.Y.Z**` (exactly once). **20 symbols, SPEC 3.0.0,
on 2026-10-22.** Both target frameworks (net8.0, net10.0) are checked against the same files by the
build. The analyzer is `PrivateAssets="all"`: `dotnet pack` was checked to list only `Wasmtime` as a
dependency.

**The build check passes `--no-incremental`** (2026-10-22): the analyzer reports only when the
compiler runs, and on exFAT an incremental build was seen to reuse stale output after an edit
(best-practices L4).

**Updating it:** `bun scripts/compat.ts surface` runs `dotnet format analyzers --diagnostics RS0016
RS0037`, which appends new symbols to Unshipped. A removed symbol is reported by the build (RS0017);
delete its Unshipped line, or add `*REMOVED*<line>` to Unshipped when it had shipped.

**The floor:** a removed line → major; an added line → minor; nothing → patch. A changed signature
is a removed line plus an added one → major; it errs high (cannot tell widening from narrowing),
same as -c and -js. The SPEC version moves it by its own level (lowered → major).

**The version line** is `<Version>` in `UniversalWasmLoader.csproj`; it stays at the last released
tag, and `bump` refuses otherwise. `bump` also refuses a dirty tree; it rewrites `<Version>`, folds
Unshipped into Shipped (keeping Shipped's order, appending new lines), and stamps the README row on a
major floor. It does not commit, tag or push.

**Previous-tag tests:** `git worktree add --detach` of the tag in the system temp dir; its `tests/`
(test code AND fixtures, unmodified) copied out without `bin/`/`obj/`; the one `ProjectReference`
rewritten to the CURRENT `UniversalWasmLoader.csproj` (exactly one match, or the gate throws);
`dotnet test`; verdict from the OUTPUT counts (`Failed: N, Passed: M`, summed over runs; green =
exit 0, M > 0, N = 0). On RED the last 15 lines are printed. Temp dirs and the worktree are always
removed.

**Reviewed exception — v0.1.0** (`UNGATED` in `compat.ts`): v0.1.0 = commit `f82e25b` (the nuspec
on nuget.org names `04b1b6b`, its pre-rewrite hash). Its tree has no `tests/` (gitignored then) and
no PublicAPI files. Rule 3 is exempt for it; its Rule 1 baseline is the first committed
`PublicAPI.Shipped.txt`, which was generated from library sources byte-identical to v0.1.0's
(`git diff --stat f82e25b HEAD -- Callbacks.cs InstancePool.cs Loader.cs WasmModule.cs Wit.cs` was
empty on 2026-10-22). **Tag `v0.1.0` → `f82e25b` pushed 2026-10-22** (owner): the commit whose
`<Version>` is 0.1.0 and whose message matches the nuspec's `04b1b6b`; GitHub's run list shows 0.1.0
was published by a `workflow_dispatch` run of `publish.yml` on `04b1b6b` (run 28137702082).

**First real use (2026-10-22):** `bump patch` against v0.1.0 → allowed 0.1.0 → 0.1.1 at floor patch
(API unchanged, 20 symbols; v0.1.0 exempt from Rule 3); only `<Version>` changed (`daa9137`, owner:
"bump … and commit"). Tagged `v0.1.1` → `8978aaf` and pushed after `release` passed locally on
the tagged HEAD; the GitHub Release (which runs `release` in CI) awaits the owner.

**Consumers (Rule 3, second half):** no known NuGet package depends on `UniversalWasmLoader`.

## Inverted end to end, 2026-10-22 (a scratch clone with local tags; nothing in the repo was broken)

| # | Mutant | Command | Result |
| --- | --- | --- | --- |
| S1 | clean tree, local tag v0.1.0 | `report` | floor patch; v0.1.0 exempt |
| S2 | clean | `bump patch` → commit, tag v0.1.1 → `release` | allowed 0.1.0 → 0.1.1 (only `<Version>` changed); release passes |
| S3 | bool RESULT decode inverted in `WasmModule.cs` (API unchanged) | `bump patch` | REFUSED: floor patch, v0.1.1's tests RED 6 pass / 1 fail (`Booleans`) |
| S4 | `Bind` removed, PublicAPI files NOT updated | `surface --check`; `bump patch` | STALE (`RS0017 … Bind(string! name) -> System.Func<object?[]!, object?>!`); REFUSED |
| S5 | same, `*REMOVED*` line added | `bump patch` / `bump minor` | REFUSED (below the floor; no row) / REFUSED (no row) — floor major, 0.x rule applied |
| S5d | same + `\| _next release_ \|` row | `bump minor` | ALLOWED → 0.2.0; row stamped `0.2.0`; `Bind` folded out of Shipped |
| S6 | that change tagged v0.1.2 / v0.2.0 | `release` | BLOCKED (patch below the floor) / passes |
| S7 | `Callbacks.Count` added (from v0.1.1) | `surface --check`; `surface`; `bump patch` / `bump minor` | STALE (RS0016); fixer added the line; REFUSED (floor minor, tests GREEN 7/7) / ALLOWED → 0.2.0, Shipped +1 line |
| S8 | dirty tree; version line 0.1.5 vs tag v0.1.1 | `bump` | refused / REFUSED (version line ≠ last tag) |

The previous-tag tests went RED for the `Bind` removal (v0.1.1's tests call it) and correctly did
not block: Rule 3 is required only below a major floor.

**The proof found four defects the self-test had not** (each fixed and committed before this table
was final; workspace 2.1):

- `bump` re-sorted `PublicAPI.Shipped.txt` ordinally, so a no-change release rewrote 3 lines →
  fold keeps order and appends (`6b6e07a`; self-test: an empty fold is byte-identical).
- analyzer findings were cut at the first `[`, hiding the end of `object?[]` symbols → pure
  `analyzerFindings` with self-test cases (`9b9f29a`).
- the csproj listed `PublicAPI.*.txt` as AdditionalFiles although the package already does; `dotnet
  format` then reported duplicate source files and silently skipped the RS0016 fix, so `surface`
  could not record a new symbol → explicit items removed (`e4c02b3`). The build check was unaffected.
- (C# version, before the Bun port) a csproj comment containing `--` made the project unloadable;
  caught by the gate's first surface run (best-practices L1).

## Not yet verified

- **`validate.yml` first ran 2026-10-22** on `62c8f44` (run 39820424737, ubuntu-latest): every step
  `success` — Setup Bun, Test, self-test, PublicAPI check. Only the step CONCLUSIONS were read (the
  public API); the logs' test/self-test COUNTS were not, since downloading logs needs an authenticated
  `gh` (workspace 1.1: a conclusion is not the count).
- **`release` first ran in CI 2026-10-22** (`publish.yml`, `workflow_dispatch` on `v0.1.1`, run
  39821337557): Setup Bun, Test, self-test and "Compatibility gate (release)" all `success` (step
  conclusions only, as above). That attempt failed at `NuGet/login@v1` (trusted-publishing policy
  locked to the pre-transfer owner ID; [overview.md](overview.md) → "Build / release flow"), not a
  gate fault. **Attempt 3 published 0.1.1** with every step `success`: the gate's first complete
  release, end to end.

## Traps met building it

- A scratch clone under the session scratchpad failed on MAX_PATH (`.git/objects/pack/*.rev`); the
  next commands then ran in the REAL repo (set its local `user.name`, created a local tag — both
  undone, nothing pushed). Clone to a short `%TEMP%` path, and guard every step with `git rev-parse
  --show-toplevel` before mutating anything.
- `dotnet10 run <file>.cs` without `--file` runs the csproj in the current directory instead.
