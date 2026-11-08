# open-work.md — what is still open (2026-11-08)

One line per item; done items are removed (their record lives in the topic file named). Items that
belong to the workspace session are listed only while not yet done there.

## Waiting on the owner

- **GitHub Support purge request: prepared, not yet submitted** (only the owner can file it, at
  support.github.com). Checked 2026-10-22: 0 forks, 0 pull requests ever, remote refs = `main` +
  `v0.1.0` + `v0.1.1` only (all post-purge), no LFS; old commits still served by hash (e.g.
  `f81cb55`, `f1623bd`). The request lists the 29 orphaned commits that were ever on GitHub (9
  before purge 1, 20 between the purges; full ids from the private bundles in the workspace's
  `cmem/local/backups/`) and gives `1588c76` / `0698e13` as the first changed commits (filter-branch
  has no filter-repo note). It names neither the personal address nor the removed paths.

## Backlog

- **.NET 11 GA (expected mid-November 2026):** `scripts/runtimes.csx` then fails in CI, and
  BLOCKS releases, until `net11.0` is a test target and `11.0.x` is installed in both workflows'
  test jobs. That needs a .NET 11 SDK locally (`dotnet10` cannot build `net11.0`). Whether the
  LIBRARY should gain a `net11.0` asset is a separate question; .NET 11 consumers get the
  `net10.0` one meanwhile.

- **Source-generator typed interface** (PORT_PROMPT item 4): not built; not required for SPEC
  conformance; `Bind` covers the destructure pattern. ([overview.md](overview.md) → "Public API")

(Done 2026-11-07 and removed: "`TryFetchWit` swallows every error", fixed under SPEC 4.0.0 §2 in
`9e131b2`; record in [overview.md](overview.md) → "Canonical ABI / SPEC conformance". Done
2026-11-08: 0.2.0 published, C30's split, keep-or-drop `net8.0` (dropped); record in
[overview.md](overview.md) → "Build / release flow". Also done 2026-11-08: the scripts moved
from Bun to `.csx` via the pinned local dotnet-script; record in
[working-rules.md](working-rules.md) → "Committed scripts".)

## Waiting on the workspace session (this repo's side is done)

- **H19's template cites `cmem/overview.md:150-164`**, a stale line range: the two purge records
  are further down now. Better cited by heading ("History rewritten to remove personal
  information" / "Second purge, same day"), which does not drift.
- **Ledger answers to verify and close** (`../../cmem/workspace-correspondence.md`, re-read
  2026-11-08). Each holds this repo's answer; only the row's creator closes it:
  - **C29** (loader-js closes): SPEC 4.0.0 done, 0.2.0 published.
  - **C30** (S3D closes): read; the two-job `publish.yml` + hash pins done.
  - **H29** (workspace closes): -dotnet's halves answered.
  - **C32** (this repo's notes, for the workspace and loader-js): SPEC 4.0.0, the release by tag
    push, .NET 10 only, the coverage check, `dotnet10`'s reason, and the move to dotnet-script.
    It also suggests the Tooling table's C# row "C# → `.csx` via a pinned local dotnet-script".
  - Older rows already answered here (`3e58efc`, 2026-10-26) and still open on the creator's
    side: H6, H15, H22, H25, H27, H28, C8.
  - C16 was closed by this repo (its creator) on 2026-11-07.
  - Not for this repo: H19 (excludes -dotnet), C19 (no LeptonPad ids quoted here), C33 (Bun in
    WSL; this repo uses neither).
