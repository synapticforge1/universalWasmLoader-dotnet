# open-work.md — what is still open (2026-11-07)

One line per item; done items are removed (their record lives in the topic file named). Items that
belong to the workspace session are listed only while not yet done there.

## Waiting on the owner

- **Push and release SPEC 4.0.0 as 0.2.0** (C29, 2026-11-07). `d5b50c1` (surface) + `9e131b2`
  (behaviour) + the memory commit are on local `main` only (`origin/main` = `957b2c2`). The gate
  report is clean: floor major, pre-1.0 Rule 6 minor, Breaking Changes row present, v0.1.1's tests
  7/7 on the new code. Release steps: `bun scripts/compat.ts bump minor` (→ 0.2.0, stamps the
  README row), commit, tag `v0.2.0`, push, publish the GitHub Release (that runs `publish.yml`,
  which IS the nuget.org publish). See [overview.md](overview.md) → "Build / release flow".
- **C30: split `publish.yml` so the OIDC credential is in a job that runs no third-party code**
  (read 2026-11-07; reasoned, not measured). Today ONE job has `id-token: write` and also runs
  `dotnet test`, restore, build and pack. Those execute NuGet packages' MSBuild targets
  (`Microsoft.NET.Test.Sdk`, `Wasmtime`), the PublicAPI analyzer DLL and the previous tag's tests
  (the gate). Every step of a job with `id-token: write` can request the token, and
  `NuGet/login@v1` exchanges it for a 1-hour push key. The actions are pinned by tag (`@v4`, `@v2`,
  `@v1`), not by commit hash. Proposed, after the owner says yes: a `checks` job (`contents: read`,
  no `id-token`) that tests, runs the gate, packs and uploads `out/` as an artifact; then a
  `publish` job (`id-token: write`) that downloads the artifact, asserts the tag is still at
  `github.sha`, and runs only `NuGet/login` + `dotnet nuget push`, with every `uses:` pinned to a
  hash. C30 (2) does not apply: there is no `concurrency:`, and the push is the last step. (3) does
  not apply: no `bun fmt`. This is loader-dart's shape (`cb7bc26`), plus the artifact hand-off,
  because this repo's push needs the built `.nupkg`.
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
