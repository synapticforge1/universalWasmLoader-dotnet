namespace UniversalWasmLoader;

/// <summary>
/// Host import functions the WASM module calls into .NET. Register each under the camelCase WIT
/// import name (e.g. <c>"envMul"</c> for the WIT import <c>env-mul</c>). Each callback is a typed
/// delegate whose parameters/return match the import's WIT signature; the loader marshals values.
/// </summary>
/// <example>
/// <code>
/// var cbs = new Callbacks()
///     .On("envMul", (double a, double b) => a * b)
///     .On("envAdd", (int a, int b) => a + b);
/// using var m = Loader.Load("imports.wasm", cbs);
/// </code>
/// </example>
public sealed class Callbacks
{
    internal readonly Dictionary<string, Delegate> Fns = new();

    /// <summary>Register a host callback under its camelCase WIT import name. Returns the receiver for chaining.</summary>
    public Callbacks On(string name, Delegate fn)
    {
        Fns[name] = fn;
        return this;
    }
}
