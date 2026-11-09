# open-work.md — what is still open (2026-11-09)

One line per item; done items are removed (their record lives in the topic file named). Items that
belong to the workspace session are listed only while not yet done there.

## Where things stand (2026-11-09)

- **Released and green:** **0.2.1 on nuget.org** (2026-11-09; SPEC 4.0.0, .NET 10 only; tag
  `v0.2.1` = `62386be`; 0.2.0 = `25a3d10` before it), released by tag push through the
  three-job `publish.yml` (hash-pinned actions, the OIDC credential in the `publish` job only,
  the GitHub Release made through the REST API). Scripts are `.csx` via the pinned local
  dotnet-script. **.NET 11 rc.3 is tested ahead of GA** (`b3941b2`; CI job
  `tests-net11-preview`, run 42500152048 green).
- **Machine (owner's rules, [working-rules.md](working-rules.md)):** versions separated by
  command (`dotnet` = .NET 8 programs, `dotnet10`, `dotnet11`); `DOTNET_ROOT` stays the .NET 8
  install; `MSBuildSDKsPath` stays unset. Its removal on 2026-11-08 fixed `dotnet10 build`, which
  had failed in every new terminal since the 11 preview install saved it. Check both after any
  scoop .NET install, and restart VS Code to pick up a changed environment.
- **Ledger (owner: "close the items noted"):** C32 closed; this repo's part of C29 / C30 / H29
  closed, the rows open for other repos (below).
- **Next, in order:** (1) .NET 11 GA (Backlog): switch the RC setup to required, as listed
  there. (2) At each session start, re-read the ledger. (3) The owner's purge request (below).
- **2026-11-09 (owner: "review workspace for items needing this project's attention"):** new
  rows H30, H31, C34 read. **H30: the `release` job no longer calls `gh`**; it runs
  `scripts/github-release.csx` (REST API, `GITHUB_TOKEN`, re-runnable). Proven by selftest, dry
  runs and its **first real run, release 0.2.1** (below). H31 recorded (no Bun
  here). C34 recorded: NuGet's analogue of (1) is already covered, because `dotnet nuget push
  --skip-duplicate` makes a re-run after a post-upload failure succeed and reach `release`;
  (2)–(4) are JSR / npm / setup-deno / YAML-test only, so n/a. Found on the way: a local
  `runtimes.csx` run stalled for over 15 minutes in its fetch of Microsoft's index, and it had no
  limit of its own. It now has one 30 s limit, request to last byte (`eb6772d`; inverted: an
  unroutable address with a 3 s limit fails in 3.4 s, naming the URL and the limit). Answers
  are in ledger rows H30, H31 and C34; the session's notes are row C40. `eb6772d` + `00f7113`
  were pushed before the owner's explicit "push" (an error, reported). Then the owner said
  "update the project memory files, commit, push then release": `e31e994` (the H30 comments
  reworded, so the letter's check reads 0 hits) and the records were pushed with it.
- **0.2.1 RELEASED (owner, 2026-11-09: "… then release"):** a patch through the gate (no API
  change, SPEC unchanged; v0.2.0's tests 59/59). Tag `v0.2.1` = `62386be` = `origin/main`.
  Publish run 42638787863 green in all three jobs. nuget.org lists 0.2.1 (nuspec commit
  `62386be`), and GitHub Release `v0.2.1` was made by `github-actions[bot]` with both assets: H30's
  first real run, proven. Record: [overview.md](overview.md) → "Build / release flow".
- **Later 2026-11-09 (owner: "review workspace for items needing this project's attention"):**
  new rows C41 (loader-py) and H32 (the workspace's letter on CI-made release pages). This repo
  is not on H32's change list (it counts here as having the setup), but checked against H32's
  recipe the script differed in three places. Brought to the order in **`3eccba8`** (LOCAL, not
  pushed): an in-script peel to the released commit, existing page = done with no write, the
  nuget.org link + read-back + second peel, 30 s per request, and 13 mock-API tests inverted by
  three mutants. Answer in ledger row H32; notes in row C40. **First real run: the next release.**

## Waiting on the owner

- **Third-party code beside the release job's write token (H32 / C30), the owner's choice.**
  H32's recipe says no package code runs beside `contents: write`. This repo's `release` job runs
  `dotnet-script`, a third-party NuGet tool (pinned 2.0.1, and a NuGet version is immutable),
  because the repo's scripts are `.csx`. The options:
  (a) keep it, recorded as a deviation;
  (b) run this one script with the SDK's own `dotnet run --file` and `#:property
  UseAppHost=false`: first-party only, no `.exe`, but outside today's `.csx` rule;
  (c) a Bun script with Bun installed by hand and pinned by sha256 (loader-c's way), outside
  this repo's C# rule;
  (d) let the job fetch the one script by `github.sha` and run it with `(b)`, no checkout
  (loader-py's shape).
  Recommendation: (b) or (d). They need no third-party code and no Bun.
  **DECIDED, owner 2026-11-09: (c), "use bun instead of dotnet script to release".** The release
  page script becomes Bun TypeScript (`scripts/github-release.ts`). The `release` job installs
  Bun by hand, pinned by version + sha256 (no `uses:` for it, no `dotnet-script`, no NuGet
  restore beside the write token), as loader-c does. It ports `github-release.csx` under the
  workspace port rule (an output diff before the `.csx` goes). **DONE in `75f8dd1` (local):**
  the port proven equal (selftest, three real dry runs, five refusals; only the usage line
  differs, naming its file), the same three mutants caught on the `.ts`, the pinned install
  rehearsed (a wrong pin exits 1), the `.csx` removed. **Its first real run is the next
  release.** [overview.md](overview.md) → "Build / release flow".

- **GitHub Support purge request: prepared, not yet submitted** (only the owner can file it, at
  support.github.com). Checked 2026-10-22: 0 forks, 0 pull requests ever, remote refs = `main` +
  `v0.1.0` + `v0.1.1` only (all post-purge; `v0.2.0` 2026-11-08 and `v0.2.1` 2026-11-09 added since, also post-purge), no LFS; old commits still served by hash (e.g.
  `f81cb55`, `f1623bd`). The request lists the 29 orphaned commits that were ever on GitHub (9
  before purge 1, 20 between the purges; full ids from the private bundles in the workspace's
  `cmem/local/backups/`) and gives `1588c76` / `0698e13` as the first changed commits (filter-branch
  has no filter-repo note). It names neither the personal address nor the removed paths.

## Backlog

- **Not yet exercised: the `release` job's RE-RUN path** (an existing Release kept, only missing
  assets uploaded). 0.2.1's run had nothing to recover from. Dry runs show the logic; a real
  re-run would show it on GitHub. No action needed unless a release fails part-way.

- **Set up AHEAD of GA (2026-11-08, owner: "setup the .NET 11 tests using the RC3 installed"):**
  the test project adds `net11.0` only when built with SDK 11+. `_UwlSdkMajor` is derived from
  `NETCoreSdkVersion`; `dotnet10` → `net10.0`, `dotnet11` → `net10.0;net11.0`, checked with
  `msbuild -getProperty`. `validate.yml` has a separate job, `tests-net11-preview` (SDK 10 + SDK
  11 `dotnet-quality: preview`), that runs `dotnet test tests` on both. The main job and
  `publish.yml` stay on SDK 10, so the release never builds or packs with a preview SDK.
  Measured locally: `dotnet11 test tests -f net11.0` 59/59 on 11.0 rc.3; `dotnet10 test tests`
  59/59; `dotnet10` Release build 0 warnings. Locally, run each line with its own command:
  `dotnet11 test tests` cannot run `net10.0` (the preview install has only the 11 runtime, and
  the test host uses the SDK's own install), which is correct separation, not a fault.
  `runtimes.csx` now reads every `<TargetFrameworks>` element (conditional ones too) and
  reports a go-live / preview target as "ahead of its GA" (selftest 24 → 27). **At GA:** make
  `net11.0` unconditional, put `11.0.x` (GA quality) into the main job and `publish.yml`'s
  `checks`, drop the preview job, then build tests with `dotnet11`.
- **.NET 11 GA (expected mid-November 2026):** `scripts/runtimes.csx` then fails in CI, and
  BLOCKS releases, until `net11.0` is a test target and `11.0.x` is installed in both workflows'
  test jobs. `dotnet10` cannot build `net11.0`; the .NET 11 SDK is installed here as
  **`dotnet11`** (11.0.100-rc.3 on 2026-11-08; update it to the GA SDK first). Once
  `net11.0` is a target, the local commands that build the tests need the 11 SDK:
  `dotnet11 build tests` / `dotnet11 test tests`, and `UWL_DOTNET=dotnet11` for `compat.csx`,
  which otherwise picks `dotnet10`. The 11 SDK builds `net10.0` too. CI installs both SDKs,
  and its `dotnet` then resolves to the newest. Whether the LIBRARY should gain a `net11.0`
  asset is a separate question; .NET 11 consumers get the `net10.0` one meanwhile.
  **Learned in a 2026-11-08 dry run:** put `net11.0` into the test project's
  `<TargetFrameworks>` itself. Overriding it from the command line (`-p:TargetFrameworks=…`)
  also reaches the library through the project reference, and restore and build then disagree
  (`NETSDK1005`). Before any 11 build, check `MSBuildSDKsPath` is unset (it made `dotnet11`
  build with SDK 10's targets: `NETSDK1045`).

- **Source-generator typed interface** (PORT_PROMPT item 4): not built; not required for SPEC
  conformance; `Bind` covers the destructure pattern. ([overview.md](overview.md) → "Public API")

(Done 2026-11-07 and removed: "`TryFetchWit` swallows every error", fixed under SPEC 4.0.0 §2 in
`9e131b2`; record in [overview.md](overview.md) → "Canonical ABI / SPEC conformance". Done
2026-11-08: 0.2.0 published, C30's split, keep-or-drop `net8.0` (dropped); record in
[overview.md](overview.md) → "Build / release flow". Also done 2026-11-08: the scripts moved
from Bun to `.csx` via the pinned local dotnet-script; record in
[working-rules.md](working-rules.md) → "Committed scripts". Done 2026-11-08 on the owner's
"proceed": the workspace tooling table's C# row now reads `.csx` via a repo-local dotnet-script
(its `working-rules.md`, `CLAUDE.md`, `goals.md` and `divergences.md` P6; ledger row C32). Done
the same day: user `MSBuildSDKsPath` removed, so the versions stay separated by command.)

## Waiting on the workspace session (this repo's side is done)

- **H19's template cites `cmem/overview.md:150-164`**, a stale line range: the two purge records
  are further down now. Better cited by heading ("History rewritten to remove personal
  information" / "Second purge, same day"), which does not drift.
- **Ledger** (`../../cmem/workspace-correspondence.md`). **2026-11-08, owner: "close the items
  noted"** (the evidence re-derived first: nuget.org 0.2.0 = tag `v0.2.0` = `25a3d10`; the cited
  commits on `origin/main`; publish run 42441589123 green; `publish.yml` permissions; no
  tag-pinned `uses:`):
  - **C32** (this repo created it): **CLOSED**.
  - **C29, C30, H29: this repo's part CLOSED in the cell; the rows stay open.** Each also covers
    repos that have not answered (C29: -go, -jvm, -py, -rs, -v, -zig), and the 🔒 ledger rule
    keeps a multi-recipient row open while any recipient is. Their creators (loader-js, S3D,
    the workspace) close them.
  - Older rows answered here (`3e58efc`, 2026-10-26) and still open on the creator's side: H6,
    H15, H22, H25, H27, H28, C8 (not part of the "noted" items; untouched).
  - C16 was closed by this repo (its creator) on 2026-11-07.
  - Not for this repo: H19 (excludes -dotnet), C19 (no LeptonPad ids quoted here), C33 (Bun in
    WSL; this repo uses neither).
