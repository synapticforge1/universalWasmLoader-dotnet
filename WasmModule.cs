using System.Text;
using Wasmtime;

namespace UniversalWasmLoader;

/// <summary>
/// A loaded WebAssembly library with its WIT interface applied. Call exports by their camelCase
/// name via <see cref="Call"/> or <see cref="Bind"/>. Dispose it to release the runtime and memory.
/// Conforms to the cross-language SPEC.md v4.0.0 (Canonical ABI, callee-allocated string returns,
/// typed-call arguments checked and never coerced).
/// </summary>
public sealed class WasmModule : IDisposable
{
    private readonly Engine _engine;
    private readonly Linker _linker;
    private readonly Store _store;
    private readonly Module _module;
    private readonly Instance _instance;
    private readonly Memory? _memory;
    private readonly Function? _realloc;
    private readonly IReadOnlyDictionary<string, (WitFunc Fn, Function Wasm)>? _exports; // null => raw (no WIT)
    private readonly HostFault _fault;
    private bool _disposed;

    internal WasmModule(Engine engine, Linker linker, Store store, Module module, Instance instance,
        IReadOnlyDictionary<string, (WitFunc Fn, Function Wasm)>? exports, HostFault fault)
    {
        _engine = engine;
        _linker = linker;
        _store = store;
        _module = module;
        _instance = instance;
        _exports = exports;
        _fault = fault;
        _memory = instance.GetMemory("memory");
        _realloc = instance.GetFunction("cabi_realloc");
    }

    /// <summary>The camelCase names of the module's WIT exports (empty for a raw, no-WIT module).</summary>
    public IReadOnlyCollection<string> Exports =>
        _exports is null ? Array.Empty<string>() : (IReadOnlyCollection<string>)_exports.Keys;

    /// <summary>
    /// Invoke an export by its camelCase name, marshalling .NET arguments to/from the WASM Canonical
    /// ABI. Each argument must be exactly the .NET type of its WIT parameter — <see cref="int"/> for
    /// <c>s32</c>, <see cref="long"/> for <c>s64</c>, <see cref="float"/> / <see cref="double"/> for
    /// <c>f32</c> / <c>f64</c>, <see cref="bool"/>, <see cref="string"/> — and is never converted
    /// (SPEC 4.0.0 §4); a wrong count or kind throws <see cref="ArgumentException"/> naming the export
    /// and the parameter. Returns the decoded result (<c>null</c> for a void export), boxed as the
    /// same types. An exception thrown by a host callback during the call reaches the caller as it is.
    /// </summary>
    public object? Call(string name, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        args ??= new object?[] { null }; // Call(name, null) passes one null argument

        if (_exports is null)
            return CallRaw(name, args);

        if (!_exports.TryGetValue(name, out var export))
            throw new ArgumentException($"export '{name}' not found in WIT interface", nameof(name));
        var (fn, wasmFn) = export;
        if (args.Length != fn.Params.Count)
            throw new ArgumentException(
                $"export '{name}' expects {fn.Params.Count} argument(s) ({Loader.Signature(fn.Params)}), got {args.Length}",
                nameof(args));
        for (int i = 0; i < fn.Params.Count; i++)
            CheckArg(fn, fn.Params[i], args[i]);

        var wasmArgs = new List<ValueBox>(fn.Params.Count);
        for (int i = 0; i < fn.Params.Count; i++)
            EncodeParam(fn.Params[i].Type, args[i]!, wasmArgs);

        var raw = _fault.Invoke(wasmFn, wasmArgs.ToArray());
        return DecodeResult(fn, raw);
    }

    /// <summary>Returns a standalone callable for a named export (approximates JS destructuring).</summary>
    public Func<object?[], object?> Bind(string name) => args => Call(name, args);

    // --- Canonical ABI: export call path (.NET -> WASM -> .NET) ---

    private static void CheckArg(WitFunc fn, WitParam p, object? v)
    {
        var want = Loader.ClrType(p.Type);
        if (v is null || v.GetType() != want)
            throw new ArgumentException(
                $"export '{fn.TsName}', parameter '{p.Name}' ({Wit.Name(p.Type)}) needs {want.Name}, got {Loader.Describe(v)}",
                "args");
    }

    private void EncodeParam(WitType t, object v, List<ValueBox> wasmArgs)
    {
        switch (t)
        {
            case WitType.S32:
                wasmArgs.Add((int)v);
                break;
            case WitType.S64:
                wasmArgs.Add((long)v);
                break;
            case WitType.F32:
                wasmArgs.Add((float)v);
                break;
            case WitType.F64:
                wasmArgs.Add((double)v);
                break;
            case WitType.Bool:
                wasmArgs.Add((bool)v ? 1 : 0);
                break;
            case WitType.String:
                var bytes = Encoding.UTF8.GetBytes((string)v);
                int ptr = Realloc(bytes.Length);
                bytes.CopyTo(Memory().GetSpan(ptr, bytes.Length));
                wasmArgs.Add(ptr);
                wasmArgs.Add(bytes.Length);
                break;
        }
    }

    private object? DecodeResult(WitFunc fn, object? raw)
    {
        if (fn.Result is not WitType rt) return null;
        return (rt, raw) switch
        {
            (WitType.S32, int v) => v,
            (WitType.S64, long v) => v,
            (WitType.F32, float v) => v,
            (WitType.F64, double v) => v,
            (WitType.Bool, int v) => v != 0,
            (WitType.String, int retArea) => DecodeStringReturn(fn, retArea),
            _ => throw new InvalidOperationException(
                $"export '{fn.TsName}' is declared -> {Wit.Name(rt)} but the WASM function returned {Loader.Describe(raw)}"),
        };
    }

    // Canonical ABI callee-allocated string return: the export returns an i32 pointer to a
    // callee-allocated [ptr, len] pair. Read it, decode (which copies the bytes), then call the
    // paired cabi_post_<name> export to release the buffer (no-op under a bump allocator).
    private string DecodeStringReturn(WitFunc fn, int retArea)
    {
        var mem = Memory();
        int strPtr = mem.ReadInt32(retArea);
        int strLen = mem.ReadInt32(retArea + 4);
        string s = mem.ReadString(strPtr, strLen, Encoding.UTF8);
        if (_instance.GetFunction("cabi_post_" + fn.TsName) is { } post)
            _fault.Invoke(post, new ValueBox[] { retArea });
        return s;
    }

    private Memory Memory() =>
        _memory ?? throw new InvalidOperationException("module uses string types but does not export 'memory'");

    private int Realloc(int len)
    {
        if (_realloc is null)
            throw new InvalidOperationException("module uses string types but does not export cabi_realloc");
        return _fault.Invoke(_realloc, new ValueBox[] { 0, 0, 1, len }) is int p
            ? p
            : throw new InvalidOperationException("cabi_realloc did not return an i32 pointer");
    }

    // A module with no companion .wit: its raw exports are the engine's own functions (SPEC §4
    // leaves them outside the argument rule); arguments marshal by their .NET type.
    private object? CallRaw(string name, object?[] args)
    {
        var wasmFn = _instance.GetFunction(name)
            ?? throw new ArgumentException($"raw export '{name}' not found", nameof(name));
        var boxes = new ValueBox[args.Length];
        for (int i = 0; i < args.Length; i++)
            boxes[i] = args[i] switch
            {
                int x => x,
                long x => x,
                float x => x,
                double x => x,
                bool b => b ? 1 : 0,
                _ => throw new ArgumentException($"raw call '{name}': unsupported argument type {args[i]?.GetType().Name ?? "null"} (a .wit file is required for strings)"),
            };
        return _fault.Invoke(wasmFn, boxes);
    }

    /// <summary>Releases the underlying Wasmtime runtime, instance, and linear memory.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store.Dispose();
        _linker.Dispose();
        _module.Dispose();
        _engine.Dispose();
    }
}
