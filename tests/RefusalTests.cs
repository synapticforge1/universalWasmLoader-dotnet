using System.Net;
using UniversalWasmLoader;
using Xunit;

namespace UniversalWasmLoader.Tests;

// SPEC 4.0.0 §8 "Refusals", row by row, plus the §2 / §11 rules they rest on. Every refusal checks
// the exception type AND that the message names what the SPEC's right-hand column names.
public class RefusalTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, name);

    // A fresh temp folder holding copies of fixtures (and whatever a test writes beside them).
    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("uwl-").FullName;
        public string Copy(string fixture, string? asName = null)
        {
            var dest = Path.Combine(Root, asName ?? fixture);
            File.Copy(Fixture(fixture), dest);
            return dest;
        }
        public string Write(string name, string text)
        {
            var dest = Path.Combine(Root, name);
            File.WriteAllText(dest, text);
            return dest;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void Names(Exception ex, params string[] parts)
    {
        foreach (var p in parts)
            Assert.Contains(p, ex.Message);
    }

    // --- §4: every WIT import needs a host callback, refused at load naming all missing ones ---

    [Fact]
    public void Imports_NoCallbacks_NamesBoth()
    {
        var ex = Assert.Throws<ArgumentException>(() => Loader.Load(Fixture("imports_50.wasm")));
        Names(ex, "envMul", "env-mul", "envAdd", "env-add");
    }

    [Fact]
    public void Imports_OnlyEnvMul_NamesEnvAdd()
    {
        var cbs = new Callbacks().On("envMul", (double a, double b) => a * b);
        var ex = Assert.Throws<ArgumentException>(() => Loader.Load(Fixture("imports_50.wasm"), cbs));
        Names(ex, "envAdd", "env-add");
        Assert.DoesNotContain("envMul", ex.Message);
    }

    [Fact]
    public void Imports_CallbacksForOtherNames_Ignored()
    {
        var cbs = new Callbacks()
            .On("envMul", (double a, double b) => a * b)
            .On("envAdd", (int a, int b) => a + b)
            .On("notImported", (int a) => a);
        using var m = Loader.Load(Fixture("imports_50.wasm"), cbs);
        Assert.Equal(17, m.Call("combine", 10, 7));
    }

    // --- §4: a callback's RETURN value must match the import's WIT result, never coerced ---

    public static TheoryData<object?, string> WrongS32Returns => new()
    {
        { null, "null" },
        { 2.9, "2.9" },
        { 2147483648L, "2147483648" },
        { true, "True" },
        { "17", "17" },
        { 17L, "17" },
    };

    [Theory]
    [MemberData(nameof(WrongS32Returns))]
    public void Imports_EnvAddReturnsWrongValue_FailsTheCall(object? value, string shown)
    {
        var cbs = new Callbacks()
            .On("envMul", (double a, double b) => a * b)
            .On("envAdd", (int a, int b) => value);
        using var m = Loader.Load(Fixture("imports_50.wasm"), cbs);
        var ex = Assert.Throws<InvalidOperationException>(() => m.Call("combine", 10, 7));
        Names(ex, "envAdd", "env-add", "s32", shown);
        // The module stays usable after the refused call.
        Assert.Equal(12.0, m.Call("scale", 3.0, 4.0));
    }

    [Fact]
    public void Imports_EnvAddDeclaredWithWrongReturnType_RefusedAtLoad()
    {
        var cbs = new Callbacks()
            .On("envMul", (double a, double b) => a * b)
            .On("envAdd", (int a, int b) => (double)(a + b));
        var ex = Assert.Throws<ArgumentException>(() => Loader.Load(Fixture("imports_50.wasm"), cbs));
        Names(ex, "envAdd", "env-add", "s32", "Double");
    }

    [Fact]
    public void Imports_EnvAddDeclaredWithWrongParameterType_RefusedAtLoad()
    {
        var cbs = new Callbacks()
            .On("envMul", (double a, double b) => a * b)
            .On("envAdd", (long a, long b) => (int)(a + b));
        var ex = Assert.Throws<ArgumentException>(() => Loader.Load(Fixture("imports_50.wasm"), cbs));
        Names(ex, "envAdd", "env-add", "p0", "s32", "Int64");
    }

    [Fact]
    public void Imports_AsyncCallback_RefusedAtLoad()
    {
        var cbs = new Callbacks()
            .On("envMul", (double a, double b) => Task.FromResult(a * b))
            .On("envAdd", (int a, int b) => a + b);
        var ex = Assert.Throws<ArgumentException>(() => Loader.Load(Fixture("imports_50.wasm"), cbs));
        Names(ex, "envMul", "env-mul", "Task");
    }

    [Fact]
    public void Imports_CallbackReturningATaskAsObject_FailsTheCall()
    {
        var cbs = new Callbacks()
            .On("envMul", (double a, double b) => (object)Task.FromResult(a * b))
            .On("envAdd", (int a, int b) => a + b);
        using var m = Loader.Load(Fixture("imports_50.wasm"), cbs);
        var ex = Assert.Throws<InvalidOperationException>(() => m.Call("scale", 3.0, 4.0));
        Names(ex, "envMul", "env-mul", "Task");
    }

    [Fact]
    public void Imports_CallbackException_ReachesTheCaller()
    {
        var cbs = new Callbacks()
            .On("envMul", (double a, double b) => a * b)
            .On("envAdd", (Func<int, int, int>)((a, b) => throw new KeyNotFoundException("from the host")));
        using var m = Loader.Load(Fixture("imports_50.wasm"), cbs);
        var ex = Assert.Throws<KeyNotFoundException>(() => m.Call("combine", 10, 7));
        Assert.Equal("from the host", ex.Message);
    }

    [Fact]
    public void Imports_StringResultImport_RefusedBeforeTheWasmIsRead()
    {
        using var dir = new TempDir();
        var wit = File.ReadAllText(Fixture("imports_50.wit"))
            .Replace("import env-mul: func(p0: f64, p1: f64) -> f64;", "import env-mul: func(p0: f64, p1: f64) -> string;");
        dir.Write("imports_50.wit", wit);
        // No .wasm beside it: reaching the .wasm read would be a FileNotFoundException instead.
        var cbs = new Callbacks()
            .On("envMul", (double a, double b) => "x")
            .On("envAdd", (int a, int b) => a + b);
        var ex = Assert.Throws<NotSupportedException>(() => Loader.Load(Path.Combine(dir.Root, "imports_50.wasm"), cbs));
        Names(ex, "env-mul", "string");
    }

    // --- §4: typed calls check arity and kinds, no coercion ---

    public static TheoryData<object?[], string> BadAddCalls => new()
    {
        { new object?[] { "x", 1 }, "parameter 'a'" },
        { new object?[] { 2.7, 1 }, "parameter 'a'" },
        { new object?[] { 1, 2147483648L }, "parameter 'b'" },
        { new object?[] { 1 }, "2 argument" },
        { new object?[] { 1, null }, "parameter 'b'" },
    };

    [Theory]
    [MemberData(nameof(BadAddCalls))]
    public void TypedCall_Add_Refused(object?[] args, string names)
    {
        using var m = Loader.Load(Fixture("math_50.wasm"));
        var ex = Assert.Throws<ArgumentException>(() => m.Call("add", args));
        Names(ex, "add", names);
    }

    [Fact]
    public void TypedCall_GreetWithInt_Refused()
    {
        using var m = Loader.Load(Fixture("strings_50.wasm"));
        var ex = Assert.Throws<ArgumentException>(() => m.Call("greet", 42));
        Names(ex, "greet", "parameter 'name'", "string");
    }

    [Fact]
    public void TypedCall_IsEvenWithString_Refused()
    {
        using var m = Loader.Load(Fixture("booleans_50.wasm"));
        var ex = Assert.Throws<ArgumentException>(() => m.Call("isEven", "4"));
        Names(ex, "isEven", "parameter 'n'", "s32");
    }

    [Fact]
    public void TypedCall_BoolParamGivenInt_Refused()
    {
        // The other direction of "bool is never an integer": f64 given an int is refused too.
        using var m = Loader.Load(Fixture("booleans_50.wasm"));
        var ex = Assert.Throws<ArgumentException>(() => m.Call("isPositive", 1));
        Names(ex, "isPositive", "parameter 'x'", "f64");
    }

    // --- §2: strict parsing; a .wit that was read must parse completely ---

    public static TheoryData<string, string> BadMathWits => new()
    {
        { "package local:math-50;\nworld math-50 {\n  export add: func(a: s32 b: s32) -> s32;\n}\n", "malformed parameter" },
        { "package local:math-50;\nworld math-50 {\n  export add: func(a: u32, b: s32) -> s32;\n}\n", "u32" },
        { "", "empty" },
        { "world math-50 {\n  export add: func(a: s32, b: s32) -> s32;\n}\ntrailing\n", "world" },
        { "world math-50 {\n  export add: func(a: s32, a: s32) -> s32;\n}\n", "duplicate parameter" },
        { "world math-50 {\n  export add: func(a: s32, b: s32) -> s32;\n  export add: func(a: s32, b: s32) -> s32;\n}\n", "duplicate export add" },
        { "world math-50 {\n  export add: func(a: s32, b: s32) -> s32\n}\n", "missing \";\"" },
        { "world math-50 {\n  resource thing;\n}\n", "resource thing" },
        { "world math-50 {\n  export add: func(a: s32, b: s32) -> list<u8>;\n}\n", "list<u8>" },
    };

    [Theory]
    [MemberData(nameof(BadMathWits))]
    public void Wit_DoesNotParse_FailsTheLoad(string wit, string fault)
    {
        using var dir = new TempDir();
        var wasm = dir.Copy("math_50.wasm");
        var witPath = dir.Write("math_50.wit", wit);
        var ex = Assert.Throws<FormatException>(() => Loader.Load(wasm));
        Names(ex, witPath, fault);
    }

    [Fact]
    public void Wit_TrailingCommaAndComments_Accepted()
    {
        using var dir = new TempDir();
        var wasm = dir.Copy("math_50.wasm");
        dir.Write("math_50.wit",
            "// header\nworld math-50 { // open\n  export add: func(a: s32, b: s32,) -> s32; // two\n}\n");
        using var m = Loader.Load(wasm);
        Assert.Equal(7, m.Call("add", 3, 4));
    }

    [Fact]
    public void Wit_ExportTheModuleLacks_FailsTheLoad()
    {
        using var dir = new TempDir();
        var wasm = dir.Copy("math_50.wasm");
        dir.Write("math_50.wit",
            "world math-50 {\n  export add: func(a: s32, b: s32) -> s32;\n  export cube-root: func(x: f64) -> f64;\n}\n");
        var ex = Assert.Throws<InvalidOperationException>(() => Loader.Load(wasm));
        Names(ex, "cube-root", "cubeRoot");
    }

    // --- §2: only an ABSENT .wit is raw ---

    [Fact]
    public void Wit_Unreadable_FailsTheLoad()
    {
        using var dir = new TempDir();
        var wasm = dir.Copy("math_50.wasm");
        var witPath = Path.Combine(dir.Root, "math_50.wit");
        Directory.CreateDirectory(witPath); // exists, but cannot be read as a file
        var ex = Assert.Throws<IOException>(() => Loader.Load(wasm));
        Names(ex, witPath);
    }

    [Fact]
    public void Wit_PathNotEndingInWasm_HasNoWit()
    {
        using var dir = new TempDir();
        var bin = dir.Copy("math_50.wasm", "math_50.bin");
        dir.Write("math_50.bin.wit", "this is not WIT");
        using var m = Loader.Load(bin);
        Assert.Empty(m.Exports);
        Assert.Equal(7, m.Call("add", 3, 4)); // raw
    }

    // --- §2 / §11 over http ---

    [Fact]
    public async Task Http_Typed_OneRequestPerFile()
    {
        using var srv = new TestHttpServer()
            .ServeFile("/math_50.wit", Fixture("math_50.wit"))
            .ServeFile("/math_50.wasm", Fixture("math_50.wasm"));
        using var m = await Loader.ImportAsync(srv.Url("/math_50.wasm"));
        Assert.Equal(7, m.Call("add", 3, 4));
        Assert.Equal(1, srv.Count("/math_50.wit"));
        Assert.Equal(1, srv.Count("/math_50.wasm"));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Http_WitAbsent_Raw(int status)
    {
        using var srv = new TestHttpServer()
            .Serve("/math_50.wit", status)
            .ServeFile("/math_50.wasm", Fixture("math_50.wasm"));
        using var m = await Loader.ImportAsync(srv.Url("/math_50.wasm"));
        Assert.Empty(m.Exports);
        Assert.Equal(7, m.Call("add", 3, 4));
    }

    [Fact]
    public async Task Http_Wit500_FailsBeforeTheWasm()
    {
        using var srv = new TestHttpServer()
            .Serve("/math_50.wit", 500)
            .ServeFile("/math_50.wasm", Fixture("math_50.wasm"));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Loader.ImportAsync(srv.Url("/math_50.wasm")));
        Names(ex, srv.Url("/math_50.wit"), "500");
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Equal(1, srv.Count("/math_50.wit"));
        Assert.Equal(0, srv.Count("/math_50.wasm"));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Http_WasmError_NamesUrlAndStatus(int status)
    {
        using var srv = new TestHttpServer()
            .ServeFile("/math_50.wit", Fixture("math_50.wit"))
            .Serve("/math_50.wasm", status);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Loader.ImportAsync(srv.Url("/math_50.wasm")));
        Names(ex, srv.Url("/math_50.wasm"), status.ToString());
        Assert.Equal(1, srv.Count("/math_50.wit"));
        Assert.Equal(1, srv.Count("/math_50.wasm"));
    }

    [Fact]
    public async Task Http_StringResultImport_FailsBeforeTheWasm()
    {
        var wit = File.ReadAllText(Fixture("imports_50.wit"))
            .Replace("import env-mul: func(p0: f64, p1: f64) -> f64;", "import env-mul: func(p0: f64, p1: f64) -> string;");
        using var srv = new TestHttpServer()
            .Serve("/imports_50.wit", 200, System.Text.Encoding.UTF8.GetBytes(wit))
            .ServeFile("/imports_50.wasm", Fixture("imports_50.wasm"));
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => Loader.ImportAsync(srv.Url("/imports_50.wasm")));
        Names(ex, "env-mul");
        Assert.Equal(0, srv.Count("/imports_50.wasm"));
    }

    [Fact]
    public async Task Http_ServerNeverAnswers_TimesOut()
    {
        using var srv = new TestHttpServer()
            .Stall("/math_50.wit")
            .ServeFile("/math_50.wasm", Fixture("math_50.wasm"));
        var options = new LoadOptions { Timeout = TimeSpan.FromMilliseconds(300) };
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => Loader.ImportAsync(srv.Url("/math_50.wasm"), null, options));
        Names(ex, srv.Url("/math_50.wit"), "300 ms");
        Assert.Equal(0, srv.Count("/math_50.wasm"));
    }

    [Fact]
    public async Task Http_WasmNeverAnswers_TimesOut()
    {
        using var srv = new TestHttpServer()
            .ServeFile("/math_50.wit", Fixture("math_50.wit"))
            .Stall("/math_50.wasm");
        var options = new LoadOptions { Timeout = TimeSpan.FromMilliseconds(300) };
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => Loader.ImportAsync(srv.Url("/math_50.wasm"), null, options));
        Names(ex, srv.Url("/math_50.wasm"), "300 ms");
        Assert.Equal(1, srv.Count("/math_50.wasm"));
    }

    [Fact]
    public async Task Http_AlreadyCancelled_FailsBeforeAnyFetch()
    {
        using var srv = new TestHttpServer().ServeFile("/math_50.wasm", Fixture("math_50.wasm"));
        var options = new LoadOptions { CancellationToken = new CancellationToken(canceled: true) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Loader.ImportAsync(srv.Url("/math_50.wasm"), null, options));
        Assert.Empty(srv.Targets);
    }

    [Fact]
    public async Task Http_CancelledWhileWaiting_FailsWithTheToken()
    {
        using var srv = new TestHttpServer().Stall("/math_50.wit");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var options = new LoadOptions { CancellationToken = cts.Token };
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Loader.ImportAsync(srv.Url("/math_50.wasm"), null, options));
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task Http_QueryIsNotCarriedToTheWit()
    {
        using var srv = new TestHttpServer()
            .ServeFile("/math_50.wit", Fixture("math_50.wit"))
            .ServeFile("/math_50.wasm", Fixture("math_50.wasm"));
        using var m = await Loader.ImportAsync(srv.Url("/math_50.wasm?v=1#top"));
        Assert.Equal(7, m.Call("add", 3, 4));
        Assert.Contains("/math_50.wit", srv.Targets);
        Assert.Contains("/math_50.wasm?v=1", srv.Targets);
    }

    [Fact]
    public async Task Http_ConnectionRefused_NamesTheWitUrl()
    {
        string url;
        using (var srv = new TestHttpServer()) url = srv.Url("/math_50.wasm");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Loader.ImportAsync(url));
        Names(ex, url[..^5] + ".wit");
    }

    // --- §11: the time limit's domain ---

    [Fact]
    public void Options_TimeoutDomain()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new LoadOptions().Timeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoadOptions { Timeout = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoadOptions { Timeout = TimeSpan.FromSeconds(-1) });
        Assert.Equal(Timeout.InfiniteTimeSpan, new LoadOptions { Timeout = Timeout.InfiniteTimeSpan }.Timeout);
    }
}
