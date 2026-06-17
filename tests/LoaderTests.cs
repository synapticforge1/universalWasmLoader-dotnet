using UniversalWasmLoader;
using Xunit;

namespace UniversalWasmLoader.Tests;

// The cross-language SPEC §8 reference suite, plus Bind / singleton / pool.
public class LoaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    // SPEC §8 — math_50: numeric round-trip.
    [Fact]
    public void Math()
    {
        using var m = Loader.Load(Fixture("math_50.wasm"));
        Assert.Equal(7, m.Call("add", 3, 4));
        Assert.Equal(10.0, m.Call("multiply", 2.5, 4.0));
        Assert.Equal(25, m.Call("square", 5));
    }

    // SPEC §8 — booleans_50: bool normalization.
    [Fact]
    public void Booleans()
    {
        using var m = Loader.Load(Fixture("booleans_50.wasm"));
        Assert.Equal(true, m.Call("isPositive", 1.0));
        Assert.Equal(false, m.Call("isPositive", -1.0));
        Assert.Equal(true, m.Call("inRange", 5.0, 0.0, 10.0));
        Assert.Equal(false, m.Call("inRange", 11.0, 0.0, 10.0));
        Assert.Equal(true, m.Call("isEven", 4));
        Assert.Equal(false, m.Call("isEven", 3));
    }

    // SPEC §8 — strings_50: string param + Canonical-ABI callee-allocated return.
    [Fact]
    public void Strings()
    {
        using var m = Loader.Load(Fixture("strings_50.wasm"));
        Assert.Equal("Hello, World!", m.Call("greet", "World"));
        Assert.Equal("hihi", m.Call("shout", "hi"));
        Assert.Equal(5, m.Call("strLen", "hello"));
    }

    // SPEC §8 — imports_50: host import callbacks.
    [Fact]
    public void Imports()
    {
        var cbs = new Callbacks()
            .On("envMul", (double a, double b) => a * b)
            .On("envAdd", (int a, int b) => a + b);
        using var m = Loader.Load(Fixture("imports_50.wasm"), cbs);
        Assert.Equal(12.0, m.Call("scale", 3.0, 4.0));
        Assert.Equal(17, m.Call("combine", 10, 7));
    }

    // Pattern — Bind (destructure): a named export as a standalone callable.
    [Fact]
    public void Bind()
    {
        using var m = Loader.Load(Fixture("math_50.wasm"));
        var add = m.Bind("add");
        var square = m.Bind("square");
        Assert.Equal(7, add(new object?[] { 3, 4 }));
        Assert.Equal(36, square(new object?[] { 6 }));
    }

    // SPEC §6.1 — createSingleton returns the same instance every call.
    [Fact]
    public void Singleton()
    {
        var get = Loader.CreateSingleton(Fixture("math_50.wasm"));
        using var m1 = get();
        var m2 = get();
        Assert.Same(m1, m2);
        Assert.Equal(3, m1.Call("add", 1, 2));
    }

    // SPEC §6.2 — InstancePool: Run returns the result; concurrent Runs all succeed.
    [Fact]
    public void Pool()
    {
        using var pool = new InstancePool(Fixture("math_50.wasm"), 2);
        Assert.Equal(42, pool.Run(m => m.Call("add", 20, 22)));

        Parallel.For(0, 8, i =>
        {
            var r = pool.Run(m => m.Call("square", i));
            Assert.Equal(i * i, r);
        });
    }
}
