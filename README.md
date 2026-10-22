# universalWasmLoader-dotnet

The **.NET / C#** port of the [Universal WASM Loader](https://github.com/synapticforge1/universalWasmLoader-js)
— a WIT-aware WebAssembly loader. Load a `.wasm` library, read its companion `.wit` interface, and
call each export as a type-safe .NET method with Canonical-ABI marshalling handled for you (numbers
direct, strings encoded/decoded through linear memory, booleans normalized).

- Built on **[Wasmtime](https://github.com/bytecodealliance/wasmtime-dotnet)** (the Bytecode Alliance
  engine), with **WASI Preview 1 built in** (SPEC §10) — I/O-using libraries instantiate with zero
  config; pure-compute modules need nothing.
- Conforms to the cross-language **SPEC.md v3.0.0** (Canonical ABI, callee-allocated string returns +
  `cabi_post_<name>`).
- Targets **net8.0** and **net10.0**.

## Install

```sh
dotnet add package UniversalWasmLoader
```

## Usage

```csharp
using UniversalWasmLoader;

// Load a .wasm library. The companion .wit (math.wit) is auto-detected.
using var m = Loader.Load("math.wasm");

// Namespace pattern — call exports by camelCase name.
var sum = m.Call("add", 3, 4);          // 7  (boxed int)

// Destructure pattern — bind an export as a standalone callable.
var square = m.Bind("square");
var sq = square(new object?[] { 5 });   // 25

// Strings round-trip through the Canonical ABI automatically.
var greeting = m.Call("greet", "World"); // "Hello, World!"
```

### Host import callbacks

Register host functions the module calls into, keyed by the **camelCase** WIT import name. Each is a
typed delegate; the loader marshals values:

```csharp
var cbs = new Callbacks()
    .On("envMul", (double a, double b) => a * b)
    .On("envAdd", (int a, int b) => a + b);

using var m = Loader.Load("imports.wasm", cbs);
var v = m.Call("scale", 3.0, 4.0);  // 12.0
```

### Version pinning

Append `@N` to assert the module's exported `version` global (the C SONAME convention):

```csharp
using var m = Loader.Load("math.wasm@2"); // throws if version != 2
```

### Singleton (load once, share)

```csharp
var get = Loader.CreateSingleton("math.wasm");
var m1 = get(); // loads
var m2 = get(); // same instance: ReferenceEquals(m1, m2)
```

### Instance pool (servers / concurrency)

Each instance has its own linear memory; `Run` acquires, calls, and releases atomically:

```csharp
using var pool = new InstancePool("math.wasm", size: 4);
var result = pool.Run(m => m.Call("add", 20, 22)); // 42
```

### Loading from a URL

`Load` is synchronous and file-only. For `http(s)` sources use `ImportAsync`:

```csharp
using var m = await Loader.ImportAsync("https://example.com/math.wasm");
```

## Type mapping

| WIT type | .NET argument | .NET return |
| --- | --- | --- |
| `s32` | `int` | `int` |
| `s64` | `long` | `long` |
| `f32` | `float` | `float` |
| `f64` | `double` | `double` |
| `bool` | `bool` | `bool` |
| `string` | `string` | `string` |

`Call` returns `object?` boxing the value above (`null` for a void export). A module with no
companion `.wit` is loaded with raw exports; `Call` then marshals by .NET argument type (strings
require a `.wit`).

## Test

```sh
dotnet test tests
```

The suite (`tests/LoaderTests.cs`) covers the cross-language SPEC §8 fixtures — numeric round-trip,
bool normalization, string params/returns (Canonical ABI), host import callbacks — plus `Bind`, the
singleton, and the concurrent instance pool. The fixtures are the stamped reference set from
`universalWasmLoader-js` (`tests/fixtures.json`).

## Versioning

[SemVer](https://semver.org/). Every release passes a compatibility gate
(`bun scripts/compat.ts report`): the public API is declared in `PublicAPI.Shipped.txt` /
`PublicAPI.Unshipped.txt` (enforced at build time by `Microsoft.CodeAnalysis.PublicApiAnalyzers`),
a removed or changed member forces a major version, an added one at least a minor, and the previous
release's tests must still pass against the new code. Before 1.0.0, a
breaking change may ship as a minor version, but it is always listed below.

### ⚠️ Breaking Changes

| Version | Change |
| --- | --- |

## License

MIT (see `LICENSE`).
