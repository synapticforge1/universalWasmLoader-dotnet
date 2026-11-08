# universalWasmLoader-dotnet

The **.NET / C#** port of the [Universal WASM Loader](https://github.com/synapticforge1/universalWasmLoader-js)
— a WIT-aware WebAssembly loader. Load a `.wasm` library, read its companion `.wit` interface, and
call each export as a type-safe .NET method with Canonical-ABI marshalling handled for you (numbers
direct, strings encoded/decoded through linear memory, booleans normalized).

- Built on **[Wasmtime](https://github.com/bytecodealliance/wasmtime-dotnet)** (the Bytecode Alliance
  engine), with **WASI Preview 1 built in** (SPEC §10) — I/O-using libraries instantiate with zero
  config; pure-compute modules need nothing.
- Conforms to the cross-language **SPEC.md v4.0.0** (Canonical ABI, callee-allocated string returns +
  `cabi_post_<name>`; loud failures: only an absent `.wit` means raw exports, typed calls and host
  callbacks are checked, never coerced, and each http(s) fetch has a time limit).
- Targets **.NET 10** (`net10.0`, LTS) from 0.2.0. .NET 8 and 9 reach end of support on
  2026-11-10; use 0.1.1 there. CI checks the tested lines against Microsoft's release index: every
  supported LTS and STS line is required from its GA until 30 days before its end of support.

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
typed delegate whose parameters and result are the .NET types of the import's WIT signature (see
[Type mapping](#type-mapping); `object` is also accepted and then checked when called):

```csharp
var cbs = new Callbacks()
    .On("envMul", (double a, double b) => a * b)
    .On("envAdd", (int a, int b) => a + b);

using var m = Loader.Load("imports.wasm", cbs);
var v = m.Call("scale", 3.0, 4.0);  // 12.0
```

Every WIT `import` needs a callback: a missing one fails the load, naming all of them
(`ArgumentException`). So does a delegate whose signature does not fit the import, or one that
is asynchronous (`Task` / `ValueTask`: WASM cannot wait). An import with a `string` result is not
supported (`NotSupportedException`). If a callback typed `object` returns a value of the wrong type,
the call fails with `InvalidOperationException` naming the callback, the import, its result type and
the value. An exception thrown by a callback reaches the caller of `Call` unchanged.

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

// Each fetch (the .wit, then the .wasm) has a time limit, 30 s by default; a token cancels the load.
using var cts = new CancellationTokenSource();
using var m2 = await Loader.ImportAsync("https://example.com/math.wasm", null,
    new LoadOptions { Timeout = TimeSpan.FromSeconds(5), CancellationToken = cts.Token });
```

Each file is requested once, never retried. An HTTP error fails the load with an
`HttpRequestException` naming the status and the URL. A fetch that does not finish in time throws
`TimeoutException` naming the URL and the limit. `Timeout.InfiniteTimeSpan` disables the limit.

### When is a module raw?

The companion `.wit` is the `.wasm` path with its suffix replaced (`math.wasm` → `math.wit`; a
URL's query and fragment are dropped). Raw exports are used **only when the `.wit` is absent**: the
file does not exist, or the server answers 404 or 410. A path that does not end in `.wasm` has no
`.wit`. Anything else fails the load before the `.wasm` is read: an unreadable file, another HTTP
status, a network error, the time limit. A `.wit` that does not parse also fails the load
(`FormatException` naming the `.wit` and the fault), and so does a WIT export the module lacks.

## Type mapping

| WIT type | .NET argument | .NET return |
| --- | --- | --- |
| `s32` | `int` | `int` |
| `s64` | `long` | `long` |
| `f32` | `float` | `float` |
| `f64` | `double` | `double` |
| `bool` | `bool` | `bool` |
| `string` | `string` | `string` |

Typed calls are checked, never converted: each argument must be exactly the .NET type above (`3`
is an `int`, so an `f64` parameter needs `3.0`; a `bool` is never an integer). A wrong count or type
throws `ArgumentException` naming the export and the parameter. `Call` returns `object?` boxing the
value above (`null` for a void export). A module with no companion `.wit` is loaded with raw
exports; `Call` then marshals by .NET argument type (strings require a `.wit`).

## Test

```sh
dotnet test tests
```

This runs the suite once per target framework of the test project (today `net10.0`). Built with
a .NET 11 SDK (the release candidate until .NET 11 ships), the test project also targets
`net11.0`, so `dotnet test tests -f net11.0` runs the suite on .NET 11. CI does that in its own
job.
`dotnet tool run dotnet-script scripts/runtimes.csx` checks that every required .NET line is a
test target and is installed by each CI job that runs the tests. The repo's scripts are C# scripts
(`scripts/*.csx`) run by [dotnet-script](https://github.com/dotnet-script/dotnet-script), pinned
as a local tool in `dotnet-tools.json`; run `dotnet tool restore` once.

The suite covers the cross-language SPEC §8 fixtures (`tests/LoaderTests.cs`): numeric round-trip,
bool normalization, string params/returns (Canonical ABI) and host import callbacks, plus `Bind`,
the singleton and the concurrent instance pool. It also runs SPEC 4.0.0's "Refusals" table row by
row (`tests/RefusalTests.cs`), with the http rows served by a local test server that counts
requests. The fixtures are the stamped reference set from `universalWasmLoader-js`
(`tests/fixtures.json`).

## Versioning

[SemVer](https://semver.org/). Every release passes a compatibility gate
(`dotnet tool run dotnet-script scripts/compat.csx -- report`): the public API is declared in
`PublicAPI.Shipped.txt` /
`PublicAPI.Unshipped.txt` (enforced at build time by `Microsoft.CodeAnalysis.PublicApiAnalyzers`),
a removed or changed member forces a major version, an added one at least a minor, and the previous
release's tests must still pass against the new code. Before 1.0.0, a
breaking change may ship as a minor version, but it is always listed below.

### ⚠️ Breaking Changes

| Version | Change |
| --- | --- |
| 0.2.0 | **SPEC 4.0.0: failures are loud.** (1) A `.wit` that exists but cannot be read, or an http(s) `.wit` answering anything but 2xx / 404 / 410, now fails the load instead of loading raw exports; a `.wit` that does not parse (an unsupported type such as `u32` included) fails it too, as does a WIT export the module lacks. A path not ending in `.wasm` no longer looks for `<path>.wit`. (2) Every WIT import needs a host callback whose delegate fits the import; a missing, mistyped or asynchronous one is refused at load (it used to return 0 when called). A callback's wrong return value fails the call instead of being converted. (3) `Call` no longer converts arguments: pass exactly `int` / `long` / `float` / `double` / `bool` / `string`. (4) `ImportAsync(source, callbacks)` became `ImportAsync(source, callbacks, options)`: rebuild against the new version. Each http(s) fetch now has a 30 s limit (it was HttpClient's 100 s default), and the `.wit` is fetched before the `.wasm`. (5) **.NET 10 only:** the `net8.0` build is gone, so .NET 8 and 9 projects (both out of support on 2026-11-10) cannot install 0.2.0; they stay on 0.1.1. |

## License

MIT (see `LICENSE`).
