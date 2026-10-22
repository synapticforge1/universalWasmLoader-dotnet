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

- **H6, H7 / drift L1–L3:** close for loader-dotnet (gate built and releasing; `cmem/` and
  `tests/` tracked; `CLAUDE.md` a pointer; fixtures = the stamped -js set, checked in CI).
- **H15:** reopened for loader-dotnet by the "builds an executable" clarification; this repo's
  record already says C# → Bun ([working-rules.md](working-rules.md)), and its one script conforms.
- **H19 / H18 for loader-dotnet:** now done under the wider path definition too (second purge,
  2026-10-22, [overview.md](overview.md)). The workspace's own letters cite pre-purge hashes of this
  repo (e.g. `cmem/overview.md:150-164` line refs, `f81cb55`/`d6c1525`): the map is in overview.md.
- **P6 measurement rows** for loader-go, loader-v, loader-zig and wazmrt predate the "builds an
  executable" clarification (Go/V/Zig → Bun now), and P6's state cell still says Tooling rule 1
  "still reads 'Bun/TypeScript only …'".
- **NuGet trusted publishing after a transfer:** a policy is locked to the GitHub owner ID at its
  first successful publish, so any repo moved to the org that publishes through one needs a NEW
  policy ([overview.md](overview.md); loader-dotnet's 0.1.1 hit it).
