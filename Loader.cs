using System.Text.RegularExpressions;
using Wasmtime;

namespace UniversalWasmLoader;

/// <summary>
/// Entry points for loading WIT-aware WebAssembly libraries. <see cref="Load"/> is synchronous and
/// file-based; <see cref="ImportAsync"/> additionally accepts <c>http(s)</c> URLs. Both auto-detect
/// the companion <c>.wit</c> (replacing the <c>.wasm</c> suffix) and apply the Canonical ABI; with no
/// <c>.wit</c>, raw exports are used. An optional <c>@N</c> path suffix pins the module's exported
/// <c>version</c> global (SPEC §3).
/// </summary>
public static class Loader
{
    private static readonly Regex VersionSuffix = new(@"@(\d+)$", RegexOptions.Compiled);

    /// <summary>Load a <c>.wasm</c> library from a file path (synchronous).</summary>
    public static WasmModule Load(string path, Callbacks? callbacks = null)
    {
        var (clean, requested) = ParseVersionSuffix(path);
        var wasmBytes = File.ReadAllBytes(clean);
        var witSrc = ReadSidecarWit(clean);
        return LoadCore(wasmBytes, witSrc, clean, requested, callbacks);
    }

    /// <summary>Load a <c>.wasm</c> library from a file path or an <c>http(s)</c> URL.</summary>
    public static async Task<WasmModule> ImportAsync(string source, Callbacks? callbacks = null)
    {
        var (clean, requested) = ParseVersionSuffix(source);
        byte[] wasmBytes;
        string witSrc;
        if (clean.StartsWith("http://") || clean.StartsWith("https://"))
        {
            using var http = new HttpClient();
            wasmBytes = await http.GetByteArrayAsync(clean).ConfigureAwait(false);
            witSrc = await TryFetchWit(http, ReplaceExt(clean)).ConfigureAwait(false);
        }
        else
        {
            wasmBytes = await File.ReadAllBytesAsync(clean).ConfigureAwait(false);
            witSrc = ReadSidecarWit(clean);
        }
        return LoadCore(wasmBytes, witSrc, clean, requested, callbacks);
    }

    /// <summary>
    /// Create an accessor that loads the module on the first call and caches it; every subsequent
    /// call returns the same instance (SPEC §6.1). Thread-safe.
    /// </summary>
    public static Func<WasmModule> CreateSingleton(string path, Callbacks? callbacks = null)
    {
        var gate = new object();
        WasmModule? cached = null;
        return () =>
        {
            if (cached is not null) return cached;
            lock (gate)
            {
                cached ??= Load(path, callbacks);
            }
            return cached;
        };
    }

    // --- core instantiation ---

    internal static WasmModule LoadCore(byte[] wasmBytes, string witSrc, string path, int? requested, Callbacks? cbs)
    {
        var engine = new Engine();
        var ok = false;
        Linker? linker = null;
        Store? store = null;
        Module? module = null;
        try
        {
            linker = new Linker(engine);
            // SPEC §10: provide WASI Preview 1 (Wasmtime's built-in) so an I/O-using library
            // instantiates; unused import namespaces are ignored for pure-compute modules.
            linker.DefineWasi();
            store = new Store(engine);
            store.SetWasiConfiguration(new WasiConfiguration()
                .WithInheritedStandardOutput()
                .WithInheritedStandardError());

            ParsedWit? parsed = string.IsNullOrWhiteSpace(witSrc) ? null : Wit.Parse(witSrc);
            if (parsed is { Imports.Count: > 0 })
                RegisterImports(store, linker, parsed.Imports, cbs);

            module = Module.FromBytes(engine, "module", wasmBytes);
            var instance = linker.Instantiate(store, module);

            // SPEC §10: run the reactor initializer once before any other export.
            instance.GetFunction("_initialize")?.Invoke();

            if (requested is int want)
                AssertVersion(instance, want, path);

            IReadOnlyDictionary<string, WitFunc>? exports = null;
            if (parsed is not null)
                exports = parsed.Exports.ToDictionary(e => e.TsName);

            var m = new WasmModule(engine, linker, store, module, instance, exports);
            ok = true;
            return m;
        }
        finally
        {
            if (!ok)
            {
                store?.Dispose();
                linker?.Dispose();
                module?.Dispose();
                engine.Dispose();
            }
        }
    }

    private static void AssertVersion(Instance instance, int requested, string path)
    {
        var g = instance.GetGlobal("version")
            ?? throw new InvalidOperationException(
                $"version @{requested} requested for '{path}' but the module exports no 'version' global");
        int actual = Convert.ToInt32(g.GetValue());
        if (actual != requested)
            throw new InvalidOperationException(
                $"version mismatch for '{path}' — requested @{requested}, module exports version {actual}");
    }

    // --- import side: build dynamic "env" host functions ---

    private static void RegisterImports(Store store, Linker linker, IReadOnlyList<WitFunc> imports, Callbacks? cbs)
    {
        foreach (var imp in imports)
        {
            Delegate? userFn = null;
            cbs?.Fns.TryGetValue(imp.TsName, out userFn);
            var paramKinds = FlattenParamKinds(imp.Params);
            var resultKinds = imp.Result is WitType rt ? new[] { ResultKind(rt) } : Array.Empty<ValueKind>();

            var fn = Function.FromCallback(store,
                (Caller caller, ReadOnlySpan<ValueBox> args, Span<ValueBox> results) =>
                {
                    var jsArgs = DecodeImportArgs(caller, imp.Params, args);
                    if (userFn is null)
                    {
                        if (results.Length > 0) results[0] = (ValueBox)0;
                        return;
                    }
                    var ret = InvokeUser(userFn, jsArgs);
                    if (imp.Result is WitType r && results.Length > 0)
                        results[0] = EncodeImportResult(r, ret);
                },
                paramKinds, resultKinds);

            linker.Define("env", Wit.KebabToWasmImportKey(imp.Name), fn);
        }
    }

    private static object?[] DecodeImportArgs(Caller caller, IReadOnlyList<WitParam> ps, ReadOnlySpan<ValueBox> args)
    {
        var result = new object?[ps.Count];
        int si = 0;
        for (int i = 0; i < ps.Count; i++)
        {
            switch (ps[i].Type)
            {
                case WitType.String:
                    int ptr = args[si++].AsInt32();
                    int len = args[si++].AsInt32();
                    var mem = caller.GetMemory("memory");
                    result[i] = mem?.ReadString(ptr, len, System.Text.Encoding.UTF8) ?? "";
                    break;
                case WitType.Bool:
                    result[i] = args[si++].AsInt32() != 0;
                    break;
                case WitType.F32:
                    result[i] = args[si++].AsSingle();
                    break;
                case WitType.F64:
                    result[i] = args[si++].AsDouble();
                    break;
                case WitType.S64:
                    result[i] = args[si++].AsInt64();
                    break;
                default: // S32
                    result[i] = args[si++].AsInt32();
                    break;
            }
        }
        return result;
    }

    private static object? InvokeUser(Delegate fn, object?[] jsArgs)
    {
        var ps = fn.Method.GetParameters();
        var conv = new object?[jsArgs.Length];
        for (int i = 0; i < jsArgs.Length; i++)
            conv[i] = (i < ps.Length && jsArgs[i] is not null)
                ? Convert.ChangeType(jsArgs[i], ps[i].ParameterType)
                : jsArgs[i];
        return fn.DynamicInvoke(conv);
    }

    private static ValueBox EncodeImportResult(WitType t, object? ret) => t switch
    {
        WitType.F64 => (ValueBox)Convert.ToDouble(ret),
        WitType.F32 => (ValueBox)Convert.ToSingle(ret),
        WitType.S64 => (ValueBox)Convert.ToInt64(ret),
        WitType.Bool => (ValueBox)(Convert.ToBoolean(ret) ? 1 : 0),
        _ => (ValueBox)Convert.ToInt32(ret),
    };

    private static List<ValueKind> FlattenParamKinds(IReadOnlyList<WitParam> ps)
    {
        var kinds = new List<ValueKind>(ps.Count);
        foreach (var p in ps)
        {
            switch (p.Type)
            {
                case WitType.String:
                    kinds.Add(ValueKind.Int32);
                    kinds.Add(ValueKind.Int32);
                    break;
                case WitType.F32:
                    kinds.Add(ValueKind.Float32);
                    break;
                case WitType.F64:
                    kinds.Add(ValueKind.Float64);
                    break;
                case WitType.S64:
                    kinds.Add(ValueKind.Int64);
                    break;
                default: // S32, Bool
                    kinds.Add(ValueKind.Int32);
                    break;
            }
        }
        return kinds;
    }

    private static ValueKind ResultKind(WitType t) => t switch
    {
        WitType.F32 => ValueKind.Float32,
        WitType.F64 => ValueKind.Float64,
        WitType.S64 => ValueKind.Int64,
        _ => ValueKind.Int32, // S32, Bool, String(ptr)
    };

    // --- IO helpers ---

    private static (string clean, int? requested) ParseVersionSuffix(string path)
    {
        var m = VersionSuffix.Match(path);
        return m.Success ? (path[..^m.Value.Length], int.Parse(m.Groups[1].Value)) : (path, null);
    }

    private static string ReplaceExt(string path) =>
        path.EndsWith(".wasm") ? path[..^5] + ".wit" : path + ".wit";

    private static string ReadSidecarWit(string wasmPath)
    {
        var witPath = ReplaceExt(wasmPath);
        return File.Exists(witPath) ? File.ReadAllText(witPath) : "";
    }

    private static async Task<string> TryFetchWit(HttpClient http, string url)
    {
        try
        {
            return await http.GetStringAsync(url).ConfigureAwait(false);
        }
        catch
        {
            return "";
        }
    }
}
