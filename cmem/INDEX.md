# cmem — Portable Project Memory for universalWasmLoader-dotnet

This folder is the **authoritative, portable project memory** for `universalWasmLoader-dotnet`. It
lives inside the project tree and is **committed to the public repo** (gitignored 2026-06-24 →
tracked again 2026-10-22, owner, H7 / L1). 🔒 **No secrets and no personal information, ever**:
secrets by name only, never values; no user-profile paths, emails or machine facts (those go in the
workspace's `cmem/local/environment.md`). Check before every commit that touches `cmem/`. Keep files
small and single-topic.

## Policy

- **`cmem/` is the single home for ALL project memory.** When the owner says "**update the project
  memory**," update the matching `cmem/` topic file with the latest decisions, found bugs, design
  changes, and current state — then add/refresh its one-line pointer in the table below. Convert
  relative dates to absolute; update existing entries rather than duplicating.
- **`README` and `SPEC.md` are NOT project memory.** They are the public, user-facing docs. Keep
  internal decision logs / bug post-mortems out of them — those live here in `cmem/`.

### The "update the project memory" trigger (binding on every agent)
When the owner says **"update the project memory"** (or a synonym — "update memory", "record this"),
do BOTH: (1) revise all relevant `cmem/` files and refresh the Files-table pointers (absolute dates;
update, don't duplicate); and (2) sync the user-facing README / SPEC.md only where the change is
user-relevant — never copy internal decision logs into them.

### The "look for code issues" trigger (binding on every agent)
When the owner says **"look for code issues"** (or "code audit" / "audit the code"), perform a
comprehensive audit across tested AND untested paths for: (1) workarounds / temporary hacks;
(2) dead code; (3) bugs (wrong ABI marshalling, off-by-one pointer/length math, endianness, missing
`cabi_post` calls); and (4) silent fall-throughs (returning a default instead of erroring). Report
`file:line` + severity, fix the safe ones, and keep the test suite green (`dotnet10 test tests`).

## Files

| File | What it holds |
| --- | --- |
| [overview.md](overview.md) | What this loader port is, where the repo lives (`synapticforge1` org since 2026-10-20; nuget.org updated the Trusted Publishing owner itself), its language/runtime and toolchain (.NET 10 SDK, run as `dotnet10`), API surface, ABI/SPEC conformance status, tests (tracked; fixtures = stamped -js set since 2026-10-22; §10 fixtures untested), `CLAUDE.md` pointer (L2), the 2026-10-22 history rewrite (old → new hashes), and release flow |
| [working-rules.md](working-rules.md) | Repo rules: committed scripts are TypeScript run by Bun (C# → Bun, workspace rule 1; `dotnet-script` does not run here); one-time scratchpad scripts may be any language; LF only (`.gitattributes` + `.editorconfig`); no secrets or personal information (2026-10-22) |
| [compat-gate.md](compat-gate.md) | The SemVer compatibility gate (H6): PublicAPI files + analyzer (Rule 1), `bun scripts/compat.ts` floor/bump/release/selftest, previous-tag tests, the v0.1.0 exception, the end-to-end proof and the four defects it found; v0.1.0 tag not yet made (2026-10-22) |
| [best-practices.md](best-practices.md) | METHOD rules: this repo's own (§ L1–L3) + the workspace's 28 ★ convergent rules, copied verbatim and stamped 2026-10-22 |
