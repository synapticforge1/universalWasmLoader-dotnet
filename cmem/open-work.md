# open-work.md — what is still open (2026-11-07)

One line per item; done items are removed (their record lives in the topic file named). Items that
belong to the workspace session are listed only while not yet done there.

## Waiting on the owner

- **Publish the GitHub Release for `v0.2.0`** (owner: "yes release 0.2.0", 2026-11-07). Done here:
  `bump minor` → 0.2.0 (`93cb185`), tag `v0.2.0` → `93cb185` (lightweight, like v0.1.x),
  `compat.ts release` passed locally on the tagged HEAD, `main` + tag pushed and read back with
  `ls-remote`. NOT done: the GitHub Release itself (`gh` is not installed here). Web UI: Releases →
  Draft a new release → tag `v0.2.0` → Publish. That runs `publish.yml` (`checks` then `publish`),
  which pushes 0.2.0 to nuget.org. **First run of the split workflow and of the `NuGet/login`
  hash pin:** if the org's Actions policy lists `NuGet/login@v1` by tag, the run ends in
  `startup_failure` with nothing published. The fix is then the POLICY (allow `NuGet/login@*`),
  then a re-run: a re-run reads `publish.yml` at the tagged commit, so changing the pin instead
  needs a new commit, which means moving the tag (owner's word) or releasing 0.2.1.
- **GitHub Support purge request: prepared, not yet submitted** (only the owner can file it, at
  support.github.com). Checked 2026-10-22: 0 forks, 0 pull requests ever, remote refs = `main` +
  `v0.1.0` + `v0.1.1` only (all post-purge), no LFS; old commits still served by hash (e.g.
  `f81cb55`, `f1623bd`). The request lists the 29 orphaned commits that were ever on GitHub (9
  before purge 1, 20 between the purges; full ids from the private bundles in the workspace's
  `cmem/local/backups/`) and gives `1588c76` / `0698e13` as the first changed commits (filter-branch
  has no filter-repo note). It names neither the personal address nor the removed paths.

- **After 2026-11-10 (.NET 8 and 9 EOL): keep or drop the library's `net8.0` asset?** Dropping
  it changes what the package offers (.NET 8 / 9 consumers could no longer install new versions),
  and the PublicAPI gate does not see target frameworks, so it would need its own Breaking Changes
  row. Until then `runtimes.ts` lists net8.0 / net9.0 as notes and CI keeps testing them.

## Backlog

- **.NET 11 GA (expected mid-November 2026):** `bun scripts/runtimes.ts` then fails in CI until
  `net11.0` is a test target and `11.0.x` is in `validate.yml`. That needs a .NET 11 SDK locally
  (`dotnet10` cannot build `net11.0`). Whether the LIBRARY should gain a `net11.0` asset is a
  separate question; consumers get the `net10.0` one meanwhile.

- **Source-generator typed interface** (PORT_PROMPT item 4): not built; not required for SPEC
  conformance; `Bind` covers the destructure pattern. ([overview.md](overview.md) → "Public API")

(Done 2026-11-07 and removed: "`TryFetchWit` swallows every error", fixed under SPEC 4.0.0 §2 in
`9e131b2`; record in [overview.md](overview.md) → "Canonical ABI / SPEC conformance".)

## Waiting on the workspace session (this repo's side is done)

- **H19's template cites `cmem/overview.md:150-164`**, a stale line range: the two purge records
  are further down now. Better cited by heading ("History rewritten to remove personal
  information" / "Second purge, same day"), which does not drift.
- **Ledger answers to verify and close** (2026-11-07, `../../cmem/workspace-correspondence.md`):
  C29 (this repo's cell; loader-js closes), C30 (read; S3D closes), H29 (-dotnet's remaining
  halves), and the new row C32 with this repo's notes. C16 was closed by this repo the same day,
  since the workspace had verified and folded in its notes.
