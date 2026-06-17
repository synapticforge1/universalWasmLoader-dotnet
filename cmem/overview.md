# Overview — universalWasmLoader-dotnet

## What this is

The **.NET / C#** port of the Universal WASM Loader. It loads a `.wasm` library (the reactor/library
shape `wasmtk modc` produces), reads its WIT-described interface, and exposes each export as a
type-safe .NET method with Canonical-ABI marshalling handled for the caller (numbers direct, strings
encoded/decoded through linear memory, booleans normalized). No `.wit` → raw exports. Sibling of the
reference JS/TS loader and the Go/Rust/Python ports; conforms to the cross-language `SPEC.md`.

- **Language / runtime:** C# on .NET, built on **Wasmtime** (NuGet `Wasmtime`, the Bytecode Alliance
  wasmtime-dotnet binding). **Namespace:** `UniversalWasmLoader`; **PackageId:** `UniversalWasmLoader`.
- **Distribution:** **NuGet** (`dotnet add package UniversalWasmLoader`).

## Runtime decision — Wasmtime (confirmed 2026-06-17)

**Wasmtime NuGet** (the intended/expected dep, now confirmed in `UniversalWasmLoader.csproj`:
`<PackageReference Include="Wasmtime" Version="44.0.0" />`). Unlike Go — where a head-to-head
benchmark flipped the choice from wasmtime-go to **pure-Go wazero** (~38× lower call overhead, no
CGO) — **.NET has no production-grade pure-managed wasm runtime equivalent to wazero**, so the
P/Invoke-based Wasmtime binding stands as the right choice (and gives WASI P1 for free, SPEC §10).
Spiked here first (Wasmtime 44 restores + runs on win-x64; `add(3,4)=7`, env imports, full string ABI)
before writing the port.

**Toolchain note (this machine):** the `dotnet` on PATH is the system-wide install — **runtime-only,
no SDK**. The SDK is the scoop install of
`dotnet-sdk` (**10.0.301**, runtime 10.0.9 only). So
build/test must invoke the scoop `dotnet.exe`, and the test project targets **net10.0** (the only
runtime the SDK sees). The library multi-targets **net8.0;net10.0** for consumers.

## Current state — IMPLEMENTED + SPEC-3.0.0 conformant (2026-06-17)

Fully implemented and passing the reference suite (was a pre-implementation stub). Source (namespace
`UniversalWasmLoader`):

- `Wit.cs` — WIT parser (mirrors the JS `wit-parser.js`; `GeneratedRegex`, kebab→camel, s32 fallback).
- `WasmModule.cs` — the `WasmModule` handle: `Call` / `Bind`, Canonical-ABI param encode (incl. string
  via `cabi_realloc`) + result decode (incl. **callee-allocated string returns** — read the `[ptr,len]`
  pair, decode, then call `cabi_post_<camel>`), `Dispose`. Args marshalled to `Wasmtime.ValueBox`.
- `Loader.cs` — `Load` (sync, file) / `ImportAsync` (file or `http(s)` URL) / `CreateSingleton`; core
  instantiation; **dynamic `env` host-import functions** (`Function.FromCallback(store, untyped, paramKinds,
  resultKinds)` → decode `ValueBox` per WIT, `DynamicInvoke` the user delegate, encode result);
  `@N` version pin; WASI-P1 (`linker.DefineWasi()` + inherited stdio) + `_initialize` (SPEC §10).
- `Callbacks.cs` — `new Callbacks().On(camelName, Delegate)`.
- `InstancePool.cs` — `BlockingCollection`-backed pool of N independent instances (`Acquire`/`Release`/
  `Run`/`Size`/`Available`/`Dispose`).

### Public API

`Loader.Load(path, cbs?)` · `Loader.ImportAsync(src, cbs?)` · `Loader.CreateSingleton(path, cbs?)` ·
`WasmModule.Call(name, params object?[])` → `object?` · `WasmModule.Bind(name)` · `WasmModule.Exports`
· `WasmModule.Dispose()` · `new InstancePool(path, size, cbs?)` · `new Callbacks().On(name, Delegate)`.
Exports are looked up by **camelCase** name (SPEC name-form rule). The source-generator typed
interface (PORT_PROMPT item 4) is NOT implemented — `Bind` covers the destructure pattern; codegen is
a future add (not required for SPEC §7 conformance).

## Canonical ABI / SPEC conformance

- Built **directly against SPEC v3.0.0** (callee-allocated string returns + `cabi_post_<name>`); the
  deprecated 2.x out-param convention was skipped. Component ABI only (matches `abi.js`; numeric/bool
  handling is profile-independent; strings use `cabi_realloc`/`cabi_post`).
- Wasmtime's `Function.Invoke` returns a boxed `int` for i32 results, so bool decode is `(int) != 0`
  (no high-bit-garbage issue — that bit Go/wazero needed `uint32` masking for does not arise here).

## Tests

- **Command:** `dotnet test` (via the scoop SDK `dotnet.exe`) — **7/7 pass** (`tests/LoaderTests.cs`,
  xunit). Covers SPEC §8 (`math_50`/`booleans_50`/`strings_50`/`imports_50`, copied into `tests/` and
  copied to output) + `Bind`, singleton identity, and a concurrent `InstancePool` (size 2, `Parallel.For`
  over 8).
- **Project layout gotcha:** the library csproj's implicit `**/*.cs` glob swept up `tests/*.cs`; fixed
  with `<Compile Remove="tests/**/*.cs"/>` (+ Content/None) in `UniversalWasmLoader.csproj`.

## Build / release flow

- **Build:** `dotnet build -c Release`. **Test:** `dotnet test` (use the scoop SDK `dotnet.exe`).
- **Release:** `dotnet pack` → `dotnet nuget push` to NuGet. A `run:`-only publish workflow + the
  `NUGET_API_KEY` secret are still TODO (owner registry secrets pending — same as the other ports).
