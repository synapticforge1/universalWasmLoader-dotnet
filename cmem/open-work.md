# open-work.md — what is still open (2026-11-07)

One line per item; done items are removed (their record lives in the topic file named). Items that
belong to the workspace session are listed only while not yet done there.

## Waiting on the owner

- **The 0.2.0 publish run** (owner: "yes release 0.2.0", "option 2", "move to dotnet 10",
  2026-11-07). The tag `v0.2.0` moved from `93cb185` (which started nothing) to the memory commit
  after `eca6c78`, and was pushed; that push runs `publish.yml` (`checks` → `publish` →
  `release`). It is the first run of the tag trigger, of the `release` job, and of the
  `NuGet/login` hash pin. If the org's Actions policy lists `NuGet/login@v1` by tag, the run ends
  in `startup_failure` with nothing published. The fix is then the POLICY (allow
  `NuGet/login@*`), then a re-run: a re-run reads `publish.yml` at the tagged commit, so
  changing the pin instead needs a new commit, which means moving the tag again (owner's word) or
  releasing 0.2.1.
- **GitHub Support purge request: prepared, not yet submitted** (only the owner can file it, at
  support.github.com). Checked 2026-10-22: 0 forks, 0 pull requests ever, remote refs = `main` +
  `v0.1.0` + `v0.1.1` only (all post-purge), no LFS; old commits still served by hash (e.g.
  `f81cb55`, `f1623bd`). The request lists the 29 orphaned commits that were ever on GitHub (9
  before purge 1, 20 between the purges; full ids from the private bundles in the workspace's
  `cmem/local/backups/`) and gives `1588c76` / `0698e13` as the first changed commits (filter-branch
  has no filter-repo note). It names neither the personal address nor the removed paths.

## Backlog

- **.NET 11 GA (expected mid-November 2026):** `bun scripts/runtimes.ts` then fails in CI, and
  BLOCKS releases, until `net11.0` is a test target and `11.0.x` is installed in both workflows'
  test jobs. That needs a .NET 11 SDK locally (`dotnet10` cannot build `net11.0`). Whether the
  LIBRARY should gain a `net11.0` asset is a separate question; .NET 11 consumers get the
  `net10.0` one meanwhile.

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
