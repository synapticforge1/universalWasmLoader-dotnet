# open-work.md — what is still open (2026-10-22)

One line per item; done items are removed (their record lives in the topic file named). Items that
belong to the workspace session are listed only while not yet done there.

## Waiting on the owner

- **wasmtk `f50c8e4` is not on GitHub** (the fixture generator's commit). Under H19's order,
  wasmtk purges and pushes first, loader-js re-stamps the fixtures from a pushed commit, then the
  ports copy: expect to re-copy `tests/*.wasm`, `*.wit`, `*.wat` and `fixtures.json` from -js then
  (`bun scripts/fixtures.ts` must pass after).

## Backlog

- **Source-generator typed interface** (PORT_PROMPT item 4): not built; not required for SPEC
  conformance; `Bind` covers the destructure pattern. ([overview.md](overview.md) → "Public API")

## Waiting on the workspace session (this repo's side is done)

- **H19's template cites `cmem/overview.md:150-164`**, a stale line range: the two purge records
  are now at `overview.md:194-230` (checked 2026-10-22). Better cited by heading ("History
  rewritten to remove personal information" / "Second purge, same day"), which does not drift.

(Checked done in the workspace 2026-10-22 and removed here: H6, H7 and drift L1–L3 for
loader-dotnet; H15 closed again; H18/H19 for loader-dotnet; the P6 rows and sentence; the NuGet
transfer lesson, now letter H20 and `layout-and-packaging.md`.)
