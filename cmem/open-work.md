# open-work.md — what is still open (2026-10-22)

One line per item; done items are removed (their record lives in the topic file named). Items that
belong to the workspace session are listed only while not yet done there.

## Waiting on the owner

- **Machine paths still in public history** (workspace letter H18's definition: ANY absolute path
  on the owner's machines, not only user-named ones). Found 2026-10-22 by `git grep` over every
  commit: the old `CLAUDE.md` safe.directory line (an absolute drive path to an old checkout) in
  `0698e13` and `f1623bd`, and a home-relative SDK install path in `f1623bd:cmem/overview.md:27`. The 2026-10-22
  purge removed user-named paths and the personal address only. Purging these needs a second
  rewrite + force push: both commits predate both tags, so every later hash changes, including
  `v0.1.0` and `v0.1.1` (trees stay identical), and the published **0.1.1's NuGet metadata names
  `8978aaf`**, which would no longer exist in the repo (H19 hazard 3). The tip is clean
  (re-scanned 2026-10-22). Owner's decision. ([overview.md](overview.md) → "Build / release flow")
- **wasmtk `f50c8e4` is not on GitHub** (the fixture generator's commit). Under H19's order,
  wasmtk purges and pushes first, loader-js re-stamps the fixtures from a pushed commit, then the
  ports copy: expect to re-copy `tests/*.wasm`, `*.wit`, `*.wat` and `fixtures.json` from -js then
  (`bun scripts/fixtures.ts` must pass after).

## Backlog

- **Source-generator typed interface** (PORT_PROMPT item 4): not built; not required for SPEC
  conformance; `Bind` covers the destructure pattern. ([overview.md](overview.md) → "Public API")

## Waiting on the workspace session (this repo's side is done)

- **H6, H7 / drift L1–L3:** close for loader-dotnet (gate built and releasing; `cmem/` and
  `tests/` tracked; `CLAUDE.md` a pointer; fixtures = the stamped -js set, checked in CI).
- **H15:** reopened for loader-dotnet by the "builds an executable" clarification; this repo's
  record already says C# → Bun ([working-rules.md](working-rules.md)), and its one script conforms.
- **H19 lists loader-dotnet as "done"**: true for the scope it had, not for H18's wider path
  definition (first item above).
- **P6 measurement rows** for loader-go, loader-v, loader-zig and wazmrt predate the "builds an
  executable" clarification (Go/V/Zig → Bun now), and P6's state cell still says Tooling rule 1
  "still reads 'Bun/TypeScript only …'".
- **NuGet trusted publishing after a transfer:** a policy is locked to the GitHub owner ID at its
  first successful publish, so any repo moved to the org that publishes through one needs a NEW
  policy ([overview.md](overview.md); loader-dotnet's 0.1.1 hit it).
