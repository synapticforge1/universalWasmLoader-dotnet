using System.Collections.Concurrent;

namespace UniversalWasmLoader;

/// <summary>
/// A pool of pre-instantiated <see cref="WasmModule"/> instances — each with its own linear memory —
/// handed out one-at-a-time so no two concurrent callers share an instance (SPEC §6.2). Distributing
/// state across independent memories extends longevity under bump allocators. Appropriate for servers
/// and loop-intensive workloads.
/// </summary>
public sealed class InstancePool : IDisposable
{
    private readonly string _path;
    private readonly Callbacks? _callbacks;
    private readonly int _size;
    private readonly BlockingCollection<WasmModule> _available;
    private readonly object _initGate = new();
    private bool _initialized;
    private bool _disposed;

    /// <summary>Create a pool of <paramref name="size"/> instances (default 4). Instances are lazily created on first use.</summary>
    public InstancePool(string path, int size = 4, Callbacks? callbacks = null)
    {
        _path = path;
        _callbacks = callbacks;
        _size = size <= 0 ? 4 : size;
        _available = new BlockingCollection<WasmModule>(_size);
    }

    /// <summary>The total number of instances in the pool.</summary>
    public int Size => _size;

    /// <summary>The number of instances currently available (not checked out).</summary>
    public int Available => _available.Count;

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initGate)
        {
            if (_initialized) return;
            for (int i = 0; i < _size; i++)
                _available.Add(Loader.Load(_path, _callbacks));
            _initialized = true;
        }
    }

    /// <summary>Check out an instance, blocking until one is free.</summary>
    public WasmModule Acquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureInitialized();
        return _available.Take();
    }

    /// <summary>Return an instance to the pool.</summary>
    public void Release(WasmModule module) => _available.Add(module);

    /// <summary>Atomically acquire an instance, invoke <paramref name="fn"/>, and release it — even if it throws.</summary>
    public T Run<T>(Func<WasmModule, T> fn)
    {
        var m = Acquire();
        try
        {
            return fn(m);
        }
        finally
        {
            Release(m);
        }
    }

    /// <summary>Disposes every pooled instance. The pool must not be used afterwards.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized)
        {
            for (int i = 0; i < _size; i++)
                _available.Take().Dispose();
        }
        _available.Dispose();
    }
}
