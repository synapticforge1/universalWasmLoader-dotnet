using System.Text;
using Wasmtime;

namespace UniversalWasmLoader;

/// <summary>
/// A loaded WebAssembly library with its WIT interface applied. Call exports by their camelCase
/// name via <see cref="Call"/> or <see cref="Bind"/>. Dispose it to release the runtime and memory.
/// Conforms to the cross-language SPEC.md v3.0.0 (Canonical ABI, callee-allocated string returns).
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
    private readonly IReadOnlyDictionary<string, WitFunc>? _exports; // null => raw (no WIT)
    private bool _disposed;

    internal WasmModule(Engine engine, Linker linker, Store store, Module module, Instance instance,
        IReadOnlyDictionary<string, WitFunc>? exports)
    {
        _engine = engine;
        _linker = linker;
        _store = store;
        _module = module;
        _instance = instance;
        _exports = exports;
        _memory = instance.GetMemory("memory");
        _realloc = instance.GetFunction("cabi_realloc");
    }

    /// <summary>The camelCase names of the module's WIT exports (empty for a raw, no-WIT module).</summary>
    public IReadOnlyCollection<string> Exports =>
        _exports is null ? Array.Empty<string>() : (IReadOnlyCollection<string>)_exports.Keys;

    /// <summary>
    /// Invoke an export by its camelCase name, marshalling native .NET arguments to/from the WASM
    /// Canonical ABI. Numbers pass through (int/long ↔ s32/s64, float/double ↔ f32/f64), bool is
    /// normalized, and strings are encoded/decoded through linear memory. Returns the decoded result
    /// (<c>null</c> for a void export), boxed as <see cref="int"/>/<see cref="long"/>/<see cref="float"/>/
    /// <see cref="double"/>/<see cref="bool"/>/<see cref="string"/>.
    /// </summary>
    public object? Call(string name, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_exports is null)
            return CallRaw(name, args);

        if (!_exports.TryGetValue(name, out var fn))
            throw new ArgumentException($"export '{name}' not found in WIT interface", nameof(name));
        var wasmFn = _instance.GetFunction(fn.TsName)
            ?? throw new InvalidOperationException($"WIT declares export '{fn.TsName}' but the module has no such WASM export");
        if (args.Length != fn.Params.Count)
            throw new ArgumentException($"'{name}' expects {fn.Params.Count} argument(s), got {args.Length}");

        var wasmArgs = new List<ValueBox>(fn.Params.Count);
        for (int i = 0; i < fn.Params.Count; i++)
            EncodeParam(fn.Params[i].Type, args[i], wasmArgs);

        var raw = wasmFn.Invoke(wasmArgs.ToArray());
        return DecodeResult(fn, raw);
    }

    /// <summary>Returns a standalone callable for a named export (approximates JS destructuring).</summary>
    public Func<object?[], object?> Bind(string name) => args => Call(name, args);

    // --- Canonical ABI: export call path (.NET -> WASM -> .NET) ---

    private void EncodeParam(WitType t, object? v, List<ValueBox> wasmArgs)
    {
        switch (t)
        {
            case WitType.S32:
                wasmArgs.Add(Convert.ToInt32(v));
                break;
            case WitType.S64:
                wasmArgs.Add(Convert.ToInt64(v));
                break;
            case WitType.F32:
                wasmArgs.Add(Convert.ToSingle(v));
                break;
            case WitType.F64:
                wasmArgs.Add(Convert.ToDouble(v));
                break;
            case WitType.Bool:
                wasmArgs.Add(Convert.ToBoolean(v) ? 1 : 0);
                break;
            case WitType.String:
                var bytes = Encoding.UTF8.GetBytes(Convert.ToString(v) ?? "");
                int ptr = Realloc(bytes.Length);
                bytes.CopyTo(_memory!.GetSpan(ptr, bytes.Length));
                wasmArgs.Add(ptr);
                wasmArgs.Add(bytes.Length);
                break;
        }
    }

    private object? DecodeResult(WitFunc fn, object? raw)
    {
        if (fn.Result is not WitType rt) return null;
        return rt switch
        {
            WitType.S32 => Convert.ToInt32(raw),
            WitType.S64 => Convert.ToInt64(raw),
            WitType.F32 => Convert.ToSingle(raw),
            WitType.F64 => Convert.ToDouble(raw),
            WitType.Bool => Convert.ToInt32(raw) != 0,
            WitType.String => DecodeStringReturn(fn, Convert.ToInt32(raw)),
            _ => raw,
        };
    }

    // Canonical ABI callee-allocated string return: the export returns an i32 pointer to a
    // callee-allocated [ptr, len] pair. Read it, decode (which copies the bytes), then call the
    // paired cabi_post_<name> export to release the buffer (no-op under a bump allocator).
    private string DecodeStringReturn(WitFunc fn, int retArea)
    {
        int strPtr = _memory!.ReadInt32(retArea);
        int strLen = _memory.ReadInt32(retArea + 4);
        string s = _memory.ReadString(strPtr, strLen, Encoding.UTF8);
        _instance.GetFunction("cabi_post_" + fn.TsName)?.Invoke(retArea);
        return s;
    }

    private int Realloc(int len)
    {
        if (_realloc is null)
            throw new InvalidOperationException("module uses string types but does not export cabi_realloc");
        return Convert.ToInt32(_realloc.Invoke((ValueBox)0, (ValueBox)0, (ValueBox)1, (ValueBox)len));
    }

    // Best-effort path for a module with no companion .wit: marshal by .NET argument type.
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
                _ => throw new ArgumentException($"raw call '{name}': unsupported argument type {args[i]?.GetType().Name} (a .wit file is required for strings)"),
            };
        return wasmFn.Invoke(boxes);
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
