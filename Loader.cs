using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using Wasmtime;

namespace UniversalWasmLoader;

/// <summary>
/// Entry points for loading WIT-aware WebAssembly libraries. <see cref="Load"/> is synchronous and
/// file-based; <see cref="ImportAsync"/> additionally accepts <c>http(s)</c> URLs. Both auto-detect
/// the companion <c>.wit</c> (replacing the <c>.wasm</c> suffix) and apply the Canonical ABI. Only an
/// ABSENT <c>.wit</c> means raw exports; any other <c>.wit</c> failure fails the load before the
/// <c>.wasm</c> is read (SPEC 4.0.0 §2). An optional <c>@N</c> path suffix pins the module's
/// exported <c>version</c> global (SPEC §3).
/// </summary>
public static class Loader
{
    private static readonly Regex VersionSuffix = new(@"@(\d+)$", RegexOptions.Compiled);

    // Strict UTF-8: a .wit that is not valid UTF-8 is a parse fault, never decoded with U+FFFD.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Load a <c>.wasm</c> library from a file path (synchronous). Local files are read without a
    /// time limit, so this takes no <see cref="LoadOptions"/>.
    /// </summary>
    public static WasmModule Load(string path, Callbacks? callbacks = null)
    {
        var (clean, requested) = ParseVersionSuffix(path);
        var witPath = LocalWitPath(clean);
        var plan = Prepare(witPath is null ? null : ReadLocalWit(witPath), witPath, callbacks);
        var wasmBytes = File.ReadAllBytes(clean);
        return LoadCore(wasmBytes, plan, clean, requested);
    }

    /// <summary>
    /// Load a <c>.wasm</c> library from a file path or an <c>http(s)</c> URL. Over http(s) the
    /// <c>.wit</c> is fetched first, then the <c>.wasm</c>, once each, each within
    /// <see cref="LoadOptions.Timeout"/> (default 30 s); <see cref="LoadOptions.CancellationToken"/>
    /// cancels the load (SPEC §11).
    /// </summary>
    /// <exception cref="HttpRequestException">An HTTP status other than 2xx (or, for the <c>.wit</c>, 404 / 410), or a network error; the message names the URL.</exception>
    /// <exception cref="TimeoutException">A fetch did not complete within the time limit; the message names the URL and the limit.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task<WasmModule> ImportAsync(string source, Callbacks? callbacks = null, LoadOptions? options = null)
    {
        options ??= new LoadOptions();
        var ct = options.CancellationToken;
        ct.ThrowIfCancellationRequested();

        var (clean, requested) = ParseVersionSuffix(source);
        if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var witPath = LocalWitPath(clean);
            var localPlan = Prepare(witPath is null ? null : ReadLocalWit(witPath), witPath, callbacks);
            var localBytes = await File.ReadAllBytesAsync(clean, ct).ConfigureAwait(false);
            return LoadCore(localBytes, localPlan, clean, requested);
        }

        using var http = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var witUrl = RemoteWitUrl(new Uri(clean));
        string? witSrc = null;
        if (witUrl is not null)
        {
            var witBytes = await FetchAsync(http, witUrl, options, absentIsNull: true).ConfigureAwait(false);
            witSrc = witBytes is null ? null : DecodeWit(witBytes, witUrl);
        }
        var plan = Prepare(witSrc, witUrl, callbacks);
        var wasmBytes = (await FetchAsync(http, clean, options, absentIsNull: false).ConfigureAwait(false))!;
        return LoadCore(wasmBytes, plan, clean, requested);
    }

    /// <summary>
    /// Create an accessor that loads the module on the first call and caches it; every subsequent
    /// call returns the same instance (SPEC §6.1). Thread-safe. File-based, like <see cref="Load"/>.
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

    // --- the load plan: everything decided from the .wit and the callbacks, before the .wasm ---

    private sealed record ImportBinding(WitFunc Import, Delegate Fn);

    private sealed record Plan(ParsedWit? Wit, string? WitLabel, IReadOnlyList<ImportBinding> Bindings);

    private static Plan Prepare(string? witSrc, string? witLabel, Callbacks? cbs)
    {
        if (witSrc is null) return new Plan(null, null, Array.Empty<ImportBinding>());

        ParsedWit parsed;
        try
        {
            parsed = Wit.Parse(witSrc);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"invalid .wit '{witLabel}': {ex.Message}", ex);
        }

        // SPEC §4: an import with a string result is outside this ABI profile.
        foreach (var imp in parsed.Imports)
            if (imp.Result == WitType.String)
                throw new NotSupportedException(
                    $"WIT import {imp.Name} in '{witLabel}' has a string result, which this ABI profile does not support");

        // SPEC §4: every import needs a host callback; name all missing ones at once.
        var missing = parsed.Imports
            .Where(imp => cbs is null || !cbs.Fns.TryGetValue(imp.TsName, out var d) || d is null)
            .Select(imp => $"'{imp.TsName}' (WIT import {imp.Name})")
            .ToList();
        if (missing.Count > 0)
            throw new ArgumentException(
                $"missing host callback(s) for '{witLabel}': {string.Join(", ", missing)}", "callbacks");

        var bindings = parsed.Imports.Select(imp => new ImportBinding(imp, CheckCallback(imp, cbs!.Fns[imp.TsName]))).ToList();
        return new Plan(parsed, witLabel, bindings);
    }

    // The delegate's own signature must fit the import: one parameter per WIT parameter, each of
    // the mapped .NET type or object; a result of the mapped type or object; never asynchronous.
    private static Delegate CheckCallback(WitFunc imp, Delegate fn)
    {
        var label = $"host callback '{imp.TsName}' (WIT import {imp.Name})";
        var invoke = fn.GetType().GetMethod("Invoke")!;
        var ps = invoke.GetParameters();
        if (ps.Length != imp.Params.Count)
            throw new ArgumentException(
                $"{label} takes {ps.Length} parameter(s); the import has {imp.Params.Count} ({Signature(imp.Params)})", "callbacks");
        for (int i = 0; i < ps.Length; i++)
        {
            var want = ClrType(imp.Params[i].Type);
            var got = ps[i].ParameterType;
            if (got != want && got != typeof(object))
                throw new ArgumentException(
                    $"{label} parameter {i + 1} is {got.Name}; WIT parameter {imp.Params[i].Name} is {Wit.Name(imp.Params[i].Type)}, which needs {want.Name} (or object)", "callbacks");
        }
        var rt = invoke.ReturnType;
        if (IsAsync(rt))
            throw new ArgumentException($"{label} returns {rt.Name}, an asynchronous result; WASM cannot wait for it", "callbacks");
        if (imp.Result is WitType r)
        {
            var want = ClrType(r);
            if (rt == typeof(void))
                throw new ArgumentException($"{label} returns nothing; the import's WIT result {Wit.Name(r)} needs {want.Name} (or object)", "callbacks");
            if (rt != want && rt != typeof(object))
                throw new ArgumentException($"{label} returns {rt.Name}; the import's WIT result {Wit.Name(r)} needs {want.Name} (or object)", "callbacks");
        }
        return fn;
    }

    // --- core instantiation ---

    private static WasmModule LoadCore(byte[] wasmBytes, Plan plan, string path, int? requested)
    {
        var engine = new Engine();
        var ok = false;
        Linker? linker = null;
        Store? store = null;
        Module? module = null;
        var fault = new HostFault();
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

            foreach (var b in plan.Bindings)
                RegisterImport(store, linker, b, fault);

            module = Module.FromBytes(engine, "module", wasmBytes);
            var instance = linker.Instantiate(store, module);

            // SPEC §10: run the reactor initializer once before any other export.
            if (instance.GetFunction("_initialize") is { } init)
                fault.Invoke(init, Array.Empty<ValueBox>());

            if (requested is int want)
                AssertVersion(instance, want, path);

            Dictionary<string, (WitFunc Fn, Function Wasm)>? exports = null;
            if (plan.Wit is { } wit)
            {
                exports = new Dictionary<string, (WitFunc, Function)>(StringComparer.Ordinal);
                var absent = new List<string>();
                foreach (var e in wit.Exports)
                {
                    if (instance.GetFunction(e.TsName) is { } f) exports[e.TsName] = (e, f);
                    else absent.Add($"{e.Name} (wasm export '{e.TsName}')");
                }
                // SPEC §2: a WIT export the module does not export fails the load.
                if (absent.Count > 0)
                    throw new InvalidOperationException(
                        $"'{plan.WitLabel}' declares export(s) that '{path}' does not export: {string.Join(", ", absent)}");
            }

            var m = new WasmModule(engine, linker, store, module, instance, exports, fault);
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
        if (g.GetValue() is not int actual)
            throw new InvalidOperationException(
                $"version @{requested} requested for '{path}' but its 'version' global is {g.Kind}, not i32");
        if (actual != requested)
            throw new InvalidOperationException(
                $"version mismatch for '{path}' — requested @{requested}, module exports version {actual}");
    }

    // --- import side: build dynamic "env" host functions ---

    private static void RegisterImport(Store store, Linker linker, ImportBinding b, HostFault fault)
    {
        var imp = b.Import;
        var paramKinds = FlattenParamKinds(imp.Params);
        var resultKinds = imp.Result is WitType rt ? new[] { ResultKind(rt) } : Array.Empty<ValueKind>();

        var fn = Function.FromCallback(store,
            (Caller caller, ReadOnlySpan<ValueBox> args, Span<ValueBox> results) =>
            {
                try
                {
                    var ret = InvokeUnwrapped(b.Fn, DecodeImportArgs(caller, imp.Params, args));
                    var encoded = CheckImportResult(imp, ret);
                    if (encoded is ValueBox box) results[0] = box;
                }
                catch (Exception ex)
                {
                    // Wasmtime turns a callback exception into a trap; keep the original so it
                    // reaches the export's caller as it is (SPEC §4).
                    fault.Pending = ExceptionDispatchInfo.Capture(ex);
                    throw;
                }
            },
            paramKinds, resultKinds);

        linker.Define("env", Wit.KebabToWasmImportKey(imp.Name), fn);
    }

    private static object? InvokeUnwrapped(Delegate fn, object?[] args)
    {
        try
        {
            return fn.DynamicInvoke(args);
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw; // unreachable
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
                    var mem = caller.GetMemory("memory")
                        ?? throw new InvalidOperationException("a string import argument needs the module's exported 'memory'");
                    result[i] = mem.ReadString(ptr, len, Encoding.UTF8);
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

    // SPEC §4: the callback's value must be the import's WIT result type exactly — never coerced.
    // A void import ignores the value; an asynchronous value is refused for every import.
    private static ValueBox? CheckImportResult(WitFunc imp, object? ret)
    {
        var label = $"host callback '{imp.TsName}' (WIT import {imp.Name})";
        if (ret is not null && IsAsync(ret.GetType()))
            throw new InvalidOperationException($"{label} returned a {ret.GetType().Name}; WASM cannot wait for it");
        if (imp.Result is not WitType r) return null;
        return (r, ret) switch
        {
            (WitType.S32, int v) => (ValueBox)v,
            (WitType.S64, long v) => (ValueBox)v,
            (WitType.F32, float v) => (ValueBox)v,
            (WitType.F64, double v) => (ValueBox)v,
            (WitType.Bool, bool v) => (ValueBox)(v ? 1 : 0),
            _ => throw new InvalidOperationException(
                $"{label} must return {Wit.Name(r)} ({ClrType(r).Name}), got {Describe(ret)}"),
        };
    }

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
        _ => ValueKind.Int32, // S32, Bool (a string result is refused at load)
    };

    // --- shared helpers (also used by WasmModule) ---

    internal static Type ClrType(WitType t) => t switch
    {
        WitType.S32 => typeof(int),
        WitType.S64 => typeof(long),
        WitType.F32 => typeof(float),
        WitType.F64 => typeof(double),
        WitType.Bool => typeof(bool),
        _ => typeof(string),
    };

    internal static string Signature(IReadOnlyList<WitParam> ps) =>
        string.Join(", ", ps.Select(p => $"{p.Name}: {Wit.Name(p.Type)}"));

    internal static string Describe(object? v) => v switch
    {
        null => "null",
        string s => $"String \"{s}\"",
        _ => $"{v.GetType().Name} {Convert.ToString(v, CultureInfo.InvariantCulture)}",
    };

    private static bool IsAsync(Type t) =>
        typeof(Task).IsAssignableFrom(t)
        || t == typeof(ValueTask)
        || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ValueTask<>));

    // --- IO helpers ---

    private static (string clean, int? requested) ParseVersionSuffix(string path)
    {
        var m = VersionSuffix.Match(path);
        return m.Success ? (path[..^m.Value.Length], int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) : (path, null);
    }

    // SPEC §2: replace the .wasm suffix; a path not ending in .wasm has no companion .wit.
    private static string? LocalWitPath(string wasmPath) =>
        wasmPath.EndsWith(".wasm", StringComparison.Ordinal) ? wasmPath[..^5] + ".wit" : null;

    // SPEC §2: the URL's query and fragment are not carried over to the .wit.
    private static string? RemoteWitUrl(Uri wasmUrl)
    {
        var left = wasmUrl.GetLeftPart(UriPartial.Path);
        return left.EndsWith(".wasm", StringComparison.Ordinal) ? left[..^5] + ".wit" : null;
    }

    // Only "not found" means absent; any other read failure fails the load.
    private static string? ReadLocalWit(string witPath)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(witPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"cannot read .wit '{witPath}': {ex.Message}", ex);
        }
        return DecodeWit(bytes, witPath);
    }

    private static string DecodeWit(byte[] bytes, string witLabel)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new FormatException($"invalid .wit '{witLabel}': not valid UTF-8", ex);
        }
    }

    // One request per file (never retried), within the time limit from the request to the last
    // byte of the body (SPEC §11). For the .wit, 404 / 410 mean absent (null).
    private static async Task<byte[]?> FetchAsync(HttpClient http, string url, LoadOptions options, bool absentIsNull)
    {
        var ct = options.CancellationToken;
        var limit = options.Timeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (limit != System.Threading.Timeout.InfiniteTimeSpan) cts.CancelAfter(limit);
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            int code = (int)resp.StatusCode;
            if (absentIsNull && (code == 404 || code == 410)) return null;
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"wasmImport: HTTP {code} for {url}", null, resp.StatusCode);
            return await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException($"wasmImport: load cancelled while fetching {url}", ex, ct);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"wasmImport: no complete response from {url} within {limit.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)} ms", ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            throw new HttpRequestException($"wasmImport: fetch failed for {url}: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Carries a host callback's exception across the Wasmtime trap it causes, so the export's caller
/// receives the original exception (one per store; a store is never used concurrently).
/// </summary>
internal sealed class HostFault
{
    internal ExceptionDispatchInfo? Pending;

    internal object? Invoke(Function f, ValueBox[] args)
    {
        Pending = null;
        try
        {
            return f.Invoke(args);
        }
        catch (Exception) when (Pending is not null)
        {
            var p = Pending;
            Pending = null;
            p.Throw();
            throw; // unreachable
        }
    }
}
