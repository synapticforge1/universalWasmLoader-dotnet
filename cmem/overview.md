# Overview — universalWasmLoader-dotnet

## What this is

The **.NET / C#** port of the Universal WASM Loader. Its job is to load a WebAssembly module from a
.NET host and expose its WIT-described exports as ordinary, type-safe .NET methods — numbers pass
through directly, strings and aggregates are marshalled across the WASM Canonical ABI. It is the
.NET sibling of the reference JS/TS loader (`universalWasmLoader`) and must conform to the
cross-language `SPEC.md`.

## Maturity — EARLY STUB (as of 2026-06-15)

This repo is a skeleton, **not** an implemented loader. Present files:

- `README.md` — one line ("Universal wasm loader for .Net")
- `.gitignore` — standard .NET ignore (note: it lists `CLAUDE.md`; the cmem migration force-adds
  `CLAUDE.md` so the archive is tracked)
- `LICENSE` — MIT, Jon Marcum, 2026
- `CLAUDE.md` — auto-loaded archive (now points at `cmem/`)

There is **no `.csproj`, no `.cs` source, no test project** yet. Nothing has been published to NuGet.

## Language / runtime

- Language: **C#** on **.NET** (build/test/run via the standard `dotnet` CLI).
- WASM runtime: **NOT yet chosen in-repo.** The intended/expected dependency is **wasmtime-dotnet**
  (the .NET binding to Wasmtime, the same Bytecode Alliance engine family used across the ecosystem),
  but this is **unconfirmed** — there is no `.csproj` or `PackageReference` to verify it against. A
  future session adding the project file should record the actual chosen runtime here.
- Package registry: **NuGet** (planned; nothing published yet).

## Public API surface — NOT YET IMPLEMENTED

No types or methods exist yet. When implemented, this port should expose the idiomatic .NET
equivalents of the reference loader's surface:

- `wasmImport` → a singleton-style loader (e.g. `WasmLoader.Import(path, opts)` returning a typed
  module handle), the default "DLL" pattern.
- `createSingleton` → cached single-instance creation.
- `InstancePool` → a pool of fresh instances for server/loop workloads (`Acquire`/`Release`/`Run`,
  `Size`/`Available`/`Dispose`), mirroring the reference `InstancePool` contract.

All of the above are **missing** today.

## Canonical ABI status — NONE IMPLEMENTED

No ABI marshalling code exists. Nothing handles `cabi_realloc`, string params, string returns,
bool normalization, or `cabi_post_*`.

## SPEC.md conformance

The cross-language **`SPEC.md` is at v3.0.0 (2026-06-15)** — a **BREAKING** change. String/aggregate
RETURNS moved from the old **caller-allocated out-parameter** convention to the **canonical
callee-allocated** convention:

- The export **returns an i32 pointer** to a callee-allocated `[ptr, len]` pair.
- The host reads `ptr` and `len` from that pair, decodes the bytes, then calls the paired
  **`cabi_post_<name>(retPtr)`** export to let the module free what it allocated for that return.

**This port's string-return handling: NONE.** There is no loader code at all, so it implements
neither the OLD out-param convention nor the NEW v3.0.0 callee-allocated convention. Aligning to
SPEC 3.0.0 here is greenfield work — when the loader is first written, implement the **NEW** v3.0.0
convention directly (callee-allocated return pointer + `cabi_post_<name>`), and there is no legacy
out-param code to migrate or remove.

## Tests

None. No test project exists. Test command (once added): `dotnet test`.

## Build / release flow (planned)

1. `dotnet build` — compile the library.
2. `dotnet test` — run the suite (no tests yet).
3. `dotnet pack` → produce the `.nupkg`, publish to **NuGet** (`dotnet nuget push`). Nothing
   published yet.

## Next steps for a future session

1. Add the project: `dotnet new classlib`, set target framework, add the WASM runtime
   `PackageReference` (confirm wasmtime-dotnet) — and record the actual runtime here.
2. Implement the loader + `InstancePool` mirroring the reference loader.
3. Implement Canonical ABI marshalling to **SPEC v3.0.0** (callee-allocated returns +
   `cabi_post_<name>`).
4. Add a test project and wire `dotnet test`.
