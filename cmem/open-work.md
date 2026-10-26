# open-work.md — what is still open (2026-10-26)

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

- **Source-generator typed interface** (PORT_PROMPT item 4): not built; not required for SPEC
  conformance; `Bind` covers the destructure pattern. ([overview.md](overview.md) → "Public API")
- **`TryFetchWit` swallows every error** (`Loader.cs:266-275`, found 2026-10-26 checking loader-js's
  masked-HTTP-error finding, by reading, not running): a 500, timeout or TLS failure on the `.wit`
  is indistinguishable from "no `.wit`", and the load falls back to raw mode (★ 4.3 class). The
  `.wasm` fetch is NOT affected (`GetByteArrayAsync` throws on a non-success status). Decide
  against SPEC 3.0.0's rule for an unreachable `.wit` (likely: only 404 means absent); test with a
  local server.

## Waiting on the workspace session (this repo's side is done)

- **H19's template cites `cmem/overview.md:150-164`**, a stale line range: the two purge records
  are now at `overview.md:194-230` (checked 2026-10-22). Better cited by heading ("History
  rewritten to remove personal information" / "Second purge, same day"), which does not drift.

(Checked done in the workspace 2026-10-22 and removed here: H6, H7 and drift L1–L3 for
loader-dotnet; H15 closed again; H18/H19 for loader-dotnet; the P6 rows and sentence; the NuGet
transfer lesson, now letter H20 and `layout-and-packaging.md`.)

(Answered 2026-10-26 in the workspace ledger `../../cmem/workspace-correspondence.md`, for the
workspace to verify and close: H7 / C11 fixture re-stamp, H16, H22, H24, H25 (d), H27, H28, C8;
row C16 carries this repo's notes.)
