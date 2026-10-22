#:package System.Reflection.MetadataLoadContext@10.0.0
#:property PublishAot=false

// The compatibility gate (workspace `cmem/layout-and-packaging.md` § "The compatibility gate";
// this repo's `cmem/compat-gate.md`). "Backward compatible" is a gate result, never a judgment
// made at bump time.
//
//   dotnet10 run --file scripts/compat.cs -- surface [--check]   write / verify api-surface.json
//   dotnet10 run --file scripts/compat.cs -- report              floor + previous-tag tests vs last tag
//   dotnet10 run --file scripts/compat.cs -- bump <patch|minor|major>
//   dotnet10 run --file scripts/compat.cs -- release             CI, on the tagged commit
//   dotnet10 run --file scripts/compat.cs -- selftest            pure checks, no git/build/network
//
// Child `dotnet` processes use $UWL_DOTNET, else `dotnet10` when it is on PATH, else `dotnet`.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

var command = args.Length > 0 ? args[0] : "report";
try
{
    return command switch
    {
        "surface" => Gate.Surface(check: args.Contains("--check")),
        "report" => Gate.Report(),
        "bump" => Gate.Bump(args.Length > 1 ? args[1] : ""),
        "release" => Gate.Release(),
        "selftest" => SelfTest.Run(),
        _ => Usage(),
    };
}
catch (GateException e)
{
    Console.Error.WriteLine($"compat: {e.Message}");
    return 2;
}

static int Usage()
{
    Console.Error.WriteLine("usage: compat.cs <surface [--check] | report | bump <patch|minor|major> | release | selftest>");
    return 2;
}

sealed class GateException(string message) : Exception(message);

enum Level { Patch, Minor, Major }

readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    static readonly Regex Shape = new(@"^v?(\d+)\.(\d+)\.(\d+)$");

    public static SemVer Parse(string s)
    {
        var m = Shape.Match(s.Trim());
        if (!m.Success) throw new GateException($"not a plain SemVer version: '{s}'");
        return new(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
    }

    public static bool TryParse(string s, out SemVer v)
    {
        v = default;
        if (!Shape.IsMatch(s.Trim())) return false;
        v = Parse(s);
        return true;
    }

    public int CompareTo(SemVer o) =>
        Major != o.Major ? Major.CompareTo(o.Major) : Minor != o.Minor ? Minor.CompareTo(o.Minor) : Patch.CompareTo(o.Patch);

    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;

    public SemVer Next(Level kind) => kind switch
    {
        Level.Major => new(Major + 1, 0, 0),
        Level.Minor => new(Major, Minor + 1, 0),
        _ => new(Major, Minor, Patch + 1),
    };

    // The level a release from `from` to `to` claims; `to` must be greater.
    public static Level Step(SemVer from, SemVer to)
    {
        if (!(to > from)) throw new GateException($"{to} is not above {from}");
        return to.Major > from.Major ? Level.Major : to.Minor > from.Minor ? Level.Minor : Level.Patch;
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

record Floor(Level Level, List<string> Reasons);

record TestResult(bool Green, string Summary);

// Rule 2: the surface diff computes the floor. Removed or changed → major; added → minor;
// nothing → patch. The SPEC version moves it by its own level; lowered → major.
static class FloorOf
{
    public static Floor Compute(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        var level = Level.Patch;
        var reasons = new List<string>();
        void Raise(Level l, string why) { if (l > level) level = l; reasons.Add(why); }

        foreach (var (key, value) in before.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (key == "spec") continue;
            if (!after.TryGetValue(key, out var now)) Raise(Level.Major, $"removed: {key}");
            else if (now != value) Raise(Level.Major, $"changed: {key}: {value}  →  {now}");
        }
        foreach (var key in after.Keys.Where(k => k != "spec" && !before.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal))
            Raise(Level.Minor, $"added: {key}");

        before.TryGetValue("spec", out var specBefore);
        after.TryGetValue("spec", out var specAfter);
        if (specBefore is null || specAfter is null) throw new GateException("a surface has no 'spec' key");
        var (sb, sa) = (SemVer.Parse(specBefore), SemVer.Parse(specAfter));
        if (sa < sb) Raise(Level.Major, $"SPEC lowered: {sb} → {sa}");
        else if (sa > sb) Raise(SemVer.Step(sb, sa), $"SPEC raised: {sb} → {sa}");

        return new(level, reasons);
    }
}

static class Policy
{
    // Everything that blocks a release from `last` to `next`. Rule 6: below 1.0.0 a minor is
    // allowed where the floor says major, but the Breaking Changes row is still mandatory.
    public static List<string> Problems(SemVer last, SemVer next, Floor floor, bool breakingRow, TestResult previous)
    {
        var problems = new List<string>();
        var claimed = SemVer.Step(last, next);
        var effective = last.Major == 0 && floor.Level == Level.Major ? Level.Minor : floor.Level;
        if (claimed < effective)
            problems.Add($"{Lower(claimed)} {last} → {next} is below the floor ({Lower(floor.Level)}{(effective != floor.Level ? ", minor under the 0.x rule" : "")})");
        if (floor.Level == Level.Major && !breakingRow)
            problems.Add("the floor is major: README '### ⚠️ Breaking Changes' needs a row for this release");
        if (floor.Level != Level.Major && !previous.Green)
            problems.Add($"previous-tag tests are not green: {previous.Summary}");
        return problems;
    }

    static string Lower(Level l) => l.ToString().ToLowerInvariant();
}

static class Readme
{
    static readonly Regex Spec = new(@"\*\*SPEC\.md v(\d+\.\d+\.\d+)\*\*");
    static readonly Regex BreakingHeading = new(@"^###\s+⚠️\s*Breaking Changes\s*$", RegexOptions.Multiline);
    static readonly Regex NextHeading = new(@"^#{1,3}\s", RegexOptions.Multiline);
    public const string NextRelease = "_next release_";

    // The SPEC version this port conforms to, declared exactly once.
    public static string SpecVersion(string readme)
    {
        var matches = Spec.Matches(readme);
        if (matches.Count != 1) throw new GateException($"README must declare '**SPEC.md vX.Y.Z**' exactly once (found {matches.Count})");
        return matches[0].Groups[1].Value;
    }

    // The body of the Breaking Changes section, or null when the heading is absent.
    static (int Start, int End)? Section(string readme)
    {
        var heading = BreakingHeading.Match(readme);
        if (!heading.Success) return null;
        var start = heading.Index + heading.Length;
        var next = NextHeading.Match(readme, start);
        return (start, next.Success ? next.Index : readme.Length);
    }

    public static bool HasRow(string readme, string label)
    {
        if (Section(readme) is not { } section) return false;
        var (start, end) = section;
        return readme[start..end].Split('\n').Any(line => line.TrimStart().StartsWith($"| {label} |", StringComparison.Ordinal));
    }

    public static string StampNextRelease(string readme, SemVer version)
    {
        if (Section(readme) is not { } section || !HasRow(readme, NextRelease))
            throw new GateException($"no '| {NextRelease} |' row under '### ⚠️ Breaking Changes'");
        var (start, end) = section;
        return readme[..start] + readme[start..end].Replace($"| {NextRelease} |", $"| {version} |") + readme[end..];
    }
}

static class VersionLine
{
    static readonly Regex Line = new(@"<Version>([^<]*)</Version>");

    public static SemVer Read(string csproj)
    {
        var matches = Line.Matches(csproj);
        if (matches.Count != 1) throw new GateException($"the csproj must hold exactly one <Version> (found {matches.Count})");
        return SemVer.Parse(matches[0].Groups[1].Value);
    }

    public static string Write(string csproj, SemVer version)
    {
        Read(csproj);
        return Line.Replace(csproj, $"<Version>{version}</Version>");
    }
}

static class TestOutput
{
    static readonly Regex Counts = new(@"Failed:\s+(\d+),\s+Passed:\s+(\d+)");

    // Green needs exit 0, at least one pass and no failures (workspace 1.1: the OUTPUT, not the exit code).
    public static TestResult Judge(string output, int exitCode)
    {
        int passed = 0, failed = 0, runs = 0;
        foreach (Match m in Counts.Matches(output))
        {
            failed += int.Parse(m.Groups[1].Value);
            passed += int.Parse(m.Groups[2].Value);
            runs++;
        }
        var summary = runs == 0 ? $"no test counts in the output (exit {exitCode})" : $"{passed} passed, {failed} failed (exit {exitCode})";
        return new(exitCode == 0 && runs > 0 && passed > 0 && failed == 0, summary);
    }
}

static class Shell
{
    static string? _dotnet;

    public static string Dotnet => _dotnet ??= Environment.GetEnvironmentVariable("UWL_DOTNET") is { Length: > 0 } d ? d
        : TryRun("dotnet10", ["--version"], Directory.GetCurrentDirectory()) is { Code: 0 } ? "dotnet10" : "dotnet";

    public static (int Code, string Output) Run(string file, IEnumerable<string> arguments, string cwd)
    {
        var psi = new ProcessStartInfo(file) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        using var p = Process.Start(psi) ?? throw new GateException($"could not start {file}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    public static (int Code, string Output)? TryRun(string file, IEnumerable<string> arguments, string cwd)
    {
        try { return Run(file, arguments, cwd); }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public static string Must(string file, IEnumerable<string> arguments, string cwd)
    {
        var (code, output) = Run(file, arguments, cwd);
        if (code != 0) throw new GateException($"{file} {string.Join(' ', arguments)} failed (exit {code}):\n{Tail(output, 25)}");
        return output;
    }

    public static string Git(string cwd, params string[] arguments) => Must("git", arguments, cwd).Trim();

    public static string Tail(string text, int lines) => string.Join('\n', text.TrimEnd().Split('\n').TakeLast(lines));

    public static string TempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

// Rule 1: the surface is what a NuGet consumer can depend on — every visible type and member of
// the compiled assembly (read through MetadataLoadContext, for every target framework, which must
// agree) plus the SPEC version the README declares. A member kind it cannot key THROWS.
static class SurfaceOf
{
    const string Project = "UniversalWasmLoader.csproj";
    const string Assembly = "UniversalWasmLoader.dll";

    public static SortedDictionary<string, string> Generate(string projectDir)
    {
        var csproj = File.ReadAllText(Path.Combine(projectDir, Project));
        var tfms = Regex.Match(csproj, @"<TargetFrameworks?>([^<]+)</TargetFrameworks?>").Groups[1].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tfms.Length == 0) throw new GateException($"no target framework in {Project}");

        var tmp = Shell.TempDir("uwl-surface");
        try
        {
            SortedDictionary<string, string>? first = null;
            foreach (var tfm in tfms)
            {
                var outDir = Path.Combine(tmp, tfm);
                Shell.Must(Shell.Dotnet, ["build", Path.Combine(projectDir, Project), "-c", "Release", "-f", tfm, "-o", outDir,
                    "-p:CopyLocalLockFileAssemblies=true", "-v", "q"], projectDir);
                var surface = FromAssembly(Path.Combine(outDir, Assembly), outDir);
                if (first is null) { first = surface; continue; }
                var differ = first.Keys.Union(surface.Keys).Where(k => !first.TryGetValue(k, out var a) || !surface.TryGetValue(k, out var b) || a != b).ToList();
                if (differ.Count > 0) throw new GateException($"the surface differs between {tfms[0]} and {tfm}: {string.Join(", ", differ.Take(5))}");
            }
            first!["spec"] = Readme.SpecVersion(File.ReadAllText(Path.Combine(projectDir, "README.md")));
            return first;
        }
        finally { Shell.TryDelete(tmp); }
    }

    static SortedDictionary<string, string> FromAssembly(string dll, string outDir)
    {
        var paths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll").Concat(Directory.GetFiles(outDir, "*.dll"));
        using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths));
        var surface = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in mlc.LoadFromAssemblyPath(dll).GetTypes().Where(t => t.IsVisible))
            AddType(surface, t);
        return surface;
    }

    static bool Visible(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;
    static bool Visible(FieldInfo f) => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly;

    static void AddType(SortedDictionary<string, string> s, Type t)
    {
        var name = TypeName(t);
        var isDelegate = t.BaseType?.FullName == "System.MulticastDelegate";
        var kind = t.IsInterface ? "interface" : t.IsEnum ? "enum" : t.IsValueType ? "struct" : isDelegate ? "delegate" : "class";
        var mods = kind != "class" ? "" : t.IsAbstract && t.IsSealed ? "static " : t.IsSealed ? "sealed " : t.IsAbstract ? "abstract " : "";
        var bases = new List<string>();
        if (kind == "class" && t.BaseType is { FullName: not "System.Object" } b) bases.Add(TypeName(b));
        if (kind == "enum") bases.Add(TypeName(t.GetEnumUnderlyingType()));
        if (kind != "delegate") bases.AddRange(t.GetInterfaces().Select(TypeName).OrderBy(x => x, StringComparer.Ordinal));
        s[$"type {name}"] = $"{mods}{kind}{GenericParams(t.IsGenericTypeDefinition ? t.GetGenericArguments() : [])}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : "")}";

        if (kind == "enum")
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                s[$"enum-value {name}.{f.Name}"] = Literal(f.GetRawConstantValue());
            return;
        }
        if (isDelegate)
        {
            var invoke = t.GetMethod("Invoke") ?? throw new GateException($"delegate {name} has no Invoke");
            s[$"type {name}"] += $" {TypeName(invoke.ReturnType)}({Params(invoke)})";
            return;
        }

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var m in t.GetMembers(all))
        {
            switch (m)
            {
                case ConstructorInfo c when Visible(c) && !c.IsStatic:
                    s[$"ctor {name}({ParamTypes(c)})"] = $"{name}({Params(c)})";
                    break;
                case MethodInfo mi when Visible(mi) && (!mi.IsSpecialName || mi.Name.StartsWith("op_", StringComparison.Ordinal)):
                    var arity = mi.IsGenericMethodDefinition ? $"`{mi.GetGenericArguments().Length}" : "";
                    var shape = mi.IsStatic ? "static " : mi.IsAbstract ? "abstract " : mi.IsVirtual && !mi.IsFinal ? "virtual " : "";
                    s[$"method {name}.{mi.Name}{arity}({ParamTypes(mi)})"] =
                        $"{shape}{TypeName(mi.ReturnType)} {mi.Name}{GenericParams(mi.IsGenericMethodDefinition ? mi.GetGenericArguments() : [])}({Params(mi)})";
                    break;
                case PropertyInfo p:
                    var get = p.GetMethod is { } g && Visible(g) ? g : null;
                    var set = p.SetMethod is { } st && Visible(st) ? st : null;
                    if (get is null && set is null) break;
                    var index = p.GetIndexParameters();
                    var key = $"property {name}.{p.Name}{(index.Length > 0 ? "[" + string.Join(", ", index.Select(i => TypeName(i.ParameterType))) + "]" : "")}";
                    var stat = (get ?? set)!.IsStatic ? "static " : "";
                    s[key] = $"{stat}{TypeName(p.PropertyType)} {{ {(get is null ? "" : Access(get) + "get; ")}{(set is null ? "" : Access(set) + (IsInit(set) ? "init; " : "set; "))}}}";
                    break;
                case FieldInfo f when Visible(f):
                    var fmods = (f.IsStatic && !f.IsLiteral ? "static " : "") + (f.IsInitOnly ? "readonly " : "") + (f.IsLiteral ? "const " : "");
                    s[$"field {name}.{f.Name}"] = $"{fmods}{TypeName(f.FieldType)}{(f.IsLiteral ? " = " + Literal(f.GetRawConstantValue()) : "")}";
                    break;
                case EventInfo e:
                    if (e.AddMethod is { } add && Visible(add)) s[$"event {name}.{e.Name}"] = $"{(add.IsStatic ? "static " : "")}{TypeName(e.EventHandlerType!)}";
                    break;
                case ConstructorInfo or MethodInfo or FieldInfo:
                    break; // not visible to consumers, or an accessor keyed through its property/event
                case Type:
                    break; // nested types arrive through GetTypes()
                default:
                    throw new GateException($"cannot key member {m.Name} ({m.MemberType}) of {name}");
            }
        }
    }

    static string Access(MethodBase m) => m.IsPublic ? "" : "protected ";

    static bool IsInit(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    static string GenericParams(Type[] ps) => ps.Length == 0 ? "" : "<" + string.Join(", ", ps.Select(p => p.Name)) + ">";

    static string ParamTypes(MethodBase m) => string.Join(", ", m.GetParameters().Select(p => (p.ParameterType.IsByRef ? "ref " : "") + TypeName(p.ParameterType)));

    static string Params(MethodBase m) => string.Join(", ", m.GetParameters().Select(p =>
    {
        var isParams = p.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ParamArrayAttribute");
        var by = p.ParameterType.IsByRef ? p.IsOut ? "out " : p.IsIn ? "in " : "ref " : "";
        var dflt = p.IsOptional ? " = " + Literal(p.RawDefaultValue) : "";
        return $"{(isParams ? "params " : "")}{by}{TypeName(p.ParameterType)} {p.Name}{dflt}";
    }));

    static string Literal(object? v) => v switch
    {
        null => "null",
        DBNull => "null",
        string str => JsonSerializer.Serialize(str),
        bool bo => bo ? "true" : "false",
        char ch => $"'{ch}'",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    public static string TypeName(Type t)
    {
        if (t.IsGenericParameter) return t.Name;
        if (t.IsByRef) return TypeName(t.GetElementType()!);
        if (t.IsPointer) return TypeName(t.GetElementType()!) + "*";
        if (t.IsArray) return TypeName(t.GetElementType()!) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
        var prefix = t.IsNested ? TypeName(t.DeclaringType!) + "." : string.IsNullOrEmpty(t.Namespace) ? "" : t.Namespace + ".";
        var name = t.Name.Contains('`') ? t.Name[..t.Name.IndexOf('`')] : t.Name;
        if (!t.IsGenericType) return prefix + name;
        var all = t.GetGenericArguments();
        var own = all.Length - (t.IsNested ? t.DeclaringType!.GetGenericArguments().Length : 0);
        return own <= 0 ? prefix + name : prefix + name + "<" + string.Join(", ", all[^own..].Select(TypeName)) + ">";
    }
}

static class Gate
{
    const string SurfaceFile = "api-surface.json";
    const string Project = "UniversalWasmLoader.csproj";

    // Releases published before this gate existed, whose commit carries no tests/ (reviewed
    // exceptions, 2026-10-22: v0.1.0 = commit f82e25b, built while tests/ was gitignored).
    static readonly HashSet<string> Ungated = ["v0.1.0"];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static string Repo => Shell.Git(Directory.GetCurrentDirectory(), "rev-parse", "--show-toplevel");

    public static string Serialize(IReadOnlyDictionary<string, string> surface)
    {
        var doc = new Dictionary<string, object>
        {
            ["$comment"] = "The public surface of UniversalWasmLoader (compatibility gate Rule 1). Generated by `dotnet10 run --file scripts/compat.cs -- surface`; do not edit by hand.",
            ["symbols"] = surface,
        };
        return JsonSerializer.Serialize(doc, Json).Replace("\r\n", "\n") + "\n";
    }

    static SortedDictionary<string, string>? Committed(string repo)
    {
        var path = Path.Combine(repo, SurfaceFile);
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return new(doc.RootElement.GetProperty("symbols").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!), StringComparer.Ordinal);
    }

    static List<string> Diff(IReadOnlyDictionary<string, string> committed, IReadOnlyDictionary<string, string> now) =>
        committed.Keys.Union(now.Keys).Order(StringComparer.Ordinal).Select(k =>
            !now.TryGetValue(k, out var n) ? $"removed: {k}" :
            !committed.TryGetValue(k, out var c) ? $"added: {k}" :
            c != n ? $"changed: {k}: {c}  →  {n}" : null).OfType<string>().ToList();

    public static int Surface(bool check)
    {
        var repo = Repo;
        var now = SurfaceOf.Generate(repo);
        if (!check)
        {
            File.WriteAllText(Path.Combine(repo, SurfaceFile), Serialize(now));
            Console.WriteLine($"wrote {SurfaceFile}: {now.Count} keys (SPEC {now["spec"]})");
            return 0;
        }
        var committed = Committed(repo);
        if (committed is null) { Console.WriteLine($"STALE: {SurfaceFile} is missing"); return 1; }
        var diff = Diff(committed, now);
        if (diff.Count == 0) { Console.WriteLine($"{SurfaceFile} is current: {now.Count} keys (SPEC {now["spec"]})"); return 0; }
        Console.WriteLine($"STALE: {SurfaceFile} differs from the built assembly (Rule 5: re-baseline in its own commit):");
        foreach (var d in diff) Console.WriteLine($"  {d}");
        return 1;
    }

    static string? LastTag(string repo, bool excludeHead)
    {
        var atHead = excludeHead ? Shell.Git(repo, "tag", "--points-at", "HEAD", "--list", "v*").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet() : [];
        return Shell.Git(repo, "tag", "--merged", "HEAD", "--list", "v*").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => !atHead.Contains(t) && SemVer.TryParse(t, out _))
            .OrderByDescending(t => SemVer.Parse(t)).FirstOrDefault();
    }

    record Evaluation(string Tag, Floor Floor, TestResult Previous, List<string> Stale);

    static Evaluation Evaluate(string repo, bool excludeHead)
    {
        var tag = LastTag(repo, excludeHead) ?? throw new GateException("no v* tag is reachable from HEAD: nothing to compare against");
        var current = SurfaceOf.Generate(repo);
        var committed = Committed(repo);
        var stale = committed is null ? [$"{SurfaceFile} is missing"] : Diff(committed, current).Select(d => $"{SurfaceFile} is stale: {d}").ToList();

        var tree = Shell.TempDir("uwl-tag");
        Shell.TryDelete(tree);
        Shell.Git(repo, "worktree", "add", "--detach", tree, tag);
        try
        {
            var baseline = SurfaceOf.Generate(tree);
            var floor = FloorOf.Compute(baseline, current);
            Console.WriteLine($"last tag {tag}; floor {floor.Level.ToString().ToLowerInvariant()}{(floor.Reasons.Count == 0 ? " (surface unchanged)" : ":")}");
            foreach (var r in floor.Reasons) Console.WriteLine($"  {r}");
            var previous = PreviousTests(repo, tree, tag);
            Console.WriteLine($"previous-tag tests ({tag}'s tests/ against the current source): {(previous.Green ? "GREEN" : "RED")}, {previous.Summary}");
            return new(tag, floor, previous, stale);
        }
        finally
        {
            Shell.Run("git", ["worktree", "remove", "--force", tree], repo);
            Shell.TryDelete(tree);
        }
    }

    // Rule 3: the previous tag's tests/ (test code AND fixtures, unmodified) against the CURRENT library.
    static TestResult PreviousTests(string repo, string tagTree, string tag)
    {
        var tests = Path.Combine(tagTree, "tests");
        if (!Directory.Exists(tests))
            return Ungated.Contains(tag)
                ? new(true, $"exempt: {tag} predates the gate and its commit has no tests/ (reviewed exception)")
                : new(false, $"{tag} has no tests/ to run");

        var work = Shell.TempDir("uwl-prevtests");
        try
        {
            var copy = Path.Combine(work, "tests");
            foreach (var file in Directory.GetFiles(tests, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(tests, file);
                if (rel.Split(Path.DirectorySeparatorChar).Any(seg => seg is "bin" or "obj")) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, rel))!);
                File.Copy(file, Path.Combine(copy, rel));
            }
            var csprojPath = Directory.GetFiles(copy, "*.csproj").SingleOrDefault() ?? throw new GateException($"{tag}'s tests/ has no single .csproj");
            var text = File.ReadAllText(csprojPath);
            var reference = new Regex(@"Include=""[^""]*UniversalWasmLoader\.csproj""");
            if (reference.Matches(text).Count != 1) throw new GateException($"{tag}'s test project must reference UniversalWasmLoader.csproj exactly once");
            File.WriteAllText(csprojPath, reference.Replace(text, $"Include=\"{Path.Combine(repo, Project)}\""));

            var (code, output) = Shell.Run(Shell.Dotnet, ["test", copy], work);
            var result = TestOutput.Judge(output, code);
            if (!result.Green) Console.WriteLine(Shell.Tail(output, 15));
            return result;
        }
        finally { Shell.TryDelete(work); }
    }

    public static int Report()
    {
        var repo = Repo;
        var e = Evaluate(repo, excludeHead: false);
        foreach (var s in e.Stale) Console.WriteLine($"  {s}");
        return e.Stale.Count == 0 ? 0 : 1;
    }

    public static int Bump(string kindArg)
    {
        if (!Enum.TryParse<Level>(kindArg, ignoreCase: true, out var kind)) throw new GateException("bump needs patch, minor or major");
        var repo = Repo;
        if (Shell.Git(repo, "status", "--porcelain").Length > 0) throw new GateException("the working tree is not clean");

        var csprojPath = Path.Combine(repo, Project);
        var csproj = File.ReadAllText(csprojPath);
        var line = VersionLine.Read(csproj);
        var e = Evaluate(repo, excludeHead: false);
        var tagVersion = SemVer.Parse(e.Tag);
        var problems = new List<string>(e.Stale);
        if (line != tagVersion) problems.Add($"the version line ({line}) must equal the last tag ({e.Tag}) before a bump");
        var next = tagVersion.Next(kind);
        var readmePath = Path.Combine(repo, "README.md");
        var readme = File.ReadAllText(readmePath);
        problems.AddRange(Policy.Problems(tagVersion, next, e.Floor, Readme.HasRow(readme, Readme.NextRelease), e.Previous));

        if (problems.Count > 0)
        {
            Console.WriteLine($"REFUSED: bump {kind.ToString().ToLowerInvariant()} {tagVersion} → {next}");
            foreach (var p in problems) Console.WriteLine($"  {p}");
            return 1;
        }
        File.WriteAllText(csprojPath, VersionLine.Write(csproj, next));
        if (e.Floor.Level == Level.Major) File.WriteAllText(readmePath, Readme.StampNextRelease(readme, next));
        Console.WriteLine($"bumped {tagVersion} → {next} (floor {e.Floor.Level.ToString().ToLowerInvariant()}). Review, commit, then tag v{next}; nothing was committed, tagged or pushed.");
        return 0;
    }

    public static int Release()
    {
        var repo = Repo;
        var line = VersionLine.Read(File.ReadAllText(Path.Combine(repo, Project)));
        var headTags = Shell.Git(repo, "tag", "--points-at", "HEAD", "--list", "v*").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (!headTags.Contains($"v{line}")) throw new GateException($"HEAD is not tagged v{line} (the version line); tags here: {string.Join(", ", headTags)}");

        var e = Evaluate(repo, excludeHead: true);
        var problems = new List<string>(e.Stale);
        problems.AddRange(Policy.Problems(SemVer.Parse(e.Tag), line, e.Floor, Readme.HasRow(File.ReadAllText(Path.Combine(repo, "README.md")), line.ToString()), e.Previous));
        if (problems.Count == 0) { Console.WriteLine($"release v{line} passes the gate"); return 0; }
        Console.WriteLine($"BLOCKED: release v{line}");
        foreach (var p in problems) Console.WriteLine($"  {p}");
        return 1;
    }
}

// Pure checks of the gate's decisions: no git, no build, no network. Each must be able to fail.
static class SelfTest
{
    public static int Run()
    {
        var failed = new List<string>();
        var total = 0;
        void Check(string name, bool ok) { total++; if (!ok) failed.Add(name); }
        bool Throws(Action a) { try { a(); return false; } catch (GateException) { return true; } }

        var baseS = new Dictionary<string, string> { ["spec"] = "3.0.0", ["type A"] = "sealed class", ["method A.F()"] = "System.Int32 F()" };
        Dictionary<string, string> With(Action<Dictionary<string, string>> edit) { var d = new Dictionary<string, string>(baseS); edit(d); return d; }

        Check("floor: identical → patch", FloorOf.Compute(baseS, baseS) is { Level: Level.Patch, Reasons.Count: 0 });
        Check("floor: member added → minor", FloorOf.Compute(baseS, With(d => d["method A.G()"] = "void G()")).Level == Level.Minor);
        Check("floor: member removed → major", FloorOf.Compute(baseS, With(d => d.Remove("method A.F()"))).Level == Level.Major);
        Check("floor: member changed → major", FloorOf.Compute(baseS, With(d => d["method A.F()"] = "System.Int64 F()")).Level == Level.Major);
        Check("floor: added + removed → major", FloorOf.Compute(baseS, With(d => { d.Remove("type A"); d["type B"] = "class"; })).Level == Level.Major);
        Check("floor: SPEC patch raise → patch", FloorOf.Compute(baseS, With(d => d["spec"] = "3.0.1")).Level == Level.Patch);
        Check("floor: SPEC minor raise → minor", FloorOf.Compute(baseS, With(d => d["spec"] = "3.1.0")).Level == Level.Minor);
        Check("floor: SPEC major raise → major", FloorOf.Compute(baseS, With(d => d["spec"] = "4.0.0")).Level == Level.Major);
        Check("floor: SPEC lowered → major", FloorOf.Compute(baseS, With(d => d["spec"] = "2.9.9")).Level == Level.Major);
        Check("floor: reasons name the key", FloorOf.Compute(baseS, With(d => d.Remove("method A.F()"))).Reasons.Contains("removed: method A.F()"));
        Check("floor: missing spec throws", Throws(() => FloorOf.Compute(With(d => d.Remove("spec")), baseS)));

        var v = SemVer.Parse;
        Check("semver: parse with v", v("v1.2.3") == new SemVer(1, 2, 3));
        Check("semver: prerelease rejected", Throws(() => v("1.2.3-rc.1")));
        Check("semver: ordering 1.10.0 > 1.9.9", v("1.10.0") > v("1.9.9"));
        Check("semver: next patch", v("0.1.0").Next(Level.Patch) == v("0.1.1"));
        Check("semver: next minor resets patch", v("0.1.4").Next(Level.Minor) == v("0.2.0"));
        Check("semver: next major resets both", v("1.4.4").Next(Level.Major) == v("2.0.0"));
        Check("semver: step minor", SemVer.Step(v("0.1.0"), v("0.2.0")) == Level.Minor);
        Check("semver: step not increasing throws", Throws(() => SemVer.Step(v("0.2.0"), v("0.2.0"))));

        var green = new TestResult(true, "ok");
        var red = new TestResult(false, "1 failed");
        Floor F(Level l) => new(l, []);
        Check("policy: patch at floor patch allowed", Policy.Problems(v("1.0.0"), v("1.0.1"), F(Level.Patch), false, green).Count == 0);
        Check("policy: patch at floor minor refused", Policy.Problems(v("1.0.0"), v("1.0.1"), F(Level.Minor), false, green).Count == 1);
        Check("policy: minor at floor minor allowed", Policy.Problems(v("1.0.0"), v("1.1.0"), F(Level.Minor), false, green).Count == 0);
        Check("policy: 1.x minor at floor major refused", Policy.Problems(v("1.0.0"), v("1.1.0"), F(Level.Major), true, green).Count == 1);
        Check("policy: 0.x minor at floor major + row allowed", Policy.Problems(v("0.1.0"), v("0.2.0"), F(Level.Major), true, green).Count == 0);
        Check("policy: 0.x minor at floor major, no row refused", Policy.Problems(v("0.1.0"), v("0.2.0"), F(Level.Major), false, green).Count == 1);
        Check("policy: 0.x patch at floor major refused", Policy.Problems(v("0.1.0"), v("0.1.1"), F(Level.Major), true, green).Count == 1);
        Check("policy: major at floor major + row allowed", Policy.Problems(v("1.0.0"), v("2.0.0"), F(Level.Major), true, green).Count == 0);
        Check("policy: major at floor major, no row refused", Policy.Problems(v("1.0.0"), v("2.0.0"), F(Level.Major), false, green).Count == 1);
        Check("policy: red previous tests block a patch", Policy.Problems(v("1.0.0"), v("1.0.1"), F(Level.Patch), false, red).Count == 1);
        Check("policy: red previous tests do not block at floor major", Policy.Problems(v("1.0.0"), v("2.0.0"), F(Level.Major), true, red).Count == 0);
        Check("policy: higher than the floor is allowed", Policy.Problems(v("1.0.0"), v("1.1.0"), F(Level.Patch), false, green).Count == 0);

        const string readme = "# X\n\n- Conforms to **SPEC.md v3.0.0** (ABI)\n\n### ⚠️ Breaking Changes\n\n| Version | Change |\n| --- | --- |\n| _next release_ | removed `F` |\n| 0.2.0 | renamed `G` |\n\n## License\n\n| _next release_ | decoy |\n";
        Check("readme: spec read", Readme.SpecVersion(readme) == "3.0.0");
        Check("readme: spec twice throws", Throws(() => Readme.SpecVersion(readme + "\n**SPEC.md v3.0.0**\n")));
        Check("readme: spec absent throws", Throws(() => Readme.SpecVersion("# X\n")));
        Check("readme: next-release row found", Readme.HasRow(readme, Readme.NextRelease));
        Check("readme: version row found", Readme.HasRow(readme, "0.2.0"));
        Check("readme: missing version row", !Readme.HasRow(readme, "0.3.0"));
        Check("readme: row outside the section ignored", !Readme.HasRow(readme.Replace("| _next release_ | removed", "| x | removed"), Readme.NextRelease));
        Check("readme: no heading → no row", !Readme.HasRow("# X\n| _next release_ | y |\n", Readme.NextRelease));
        var stamped = Readme.StampNextRelease(readme, v("0.3.0"));
        Check("readme: stamp writes the version", Readme.HasRow(stamped, "0.3.0") && !Readme.HasRow(stamped, Readme.NextRelease));
        Check("readme: stamp leaves other sections alone", stamped.Contains("| _next release_ | decoy |"));
        Check("readme: stamp without a row throws", Throws(() => Readme.StampNextRelease(stamped, v("0.4.0"))));

        const string csproj = "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>";
        Check("version line: read", VersionLine.Read(csproj) == v("0.1.0"));
        Check("version line: write", VersionLine.Read(VersionLine.Write(csproj, v("0.2.0"))) == v("0.2.0"));
        Check("version line: two throw", Throws(() => VersionLine.Read(csproj + "<Version>1.0.0</Version>")));

        Check("tests: green output", TestOutput.Judge("Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7", 0).Green);
        Check("tests: a failure is red", !TestOutput.Judge("Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7", 1).Green);
        Check("tests: exit 0 with a failure is red", !TestOutput.Judge("Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7", 0).Green);
        Check("tests: zero passed is red", !TestOutput.Judge("Passed!  - Failed:     0, Passed:     0, Skipped:     0, Total:     0", 0).Green);
        Check("tests: no counts is red", !TestOutput.Judge("Build FAILED.", 0).Green);
        Check("tests: counts summed over runs", TestOutput.Judge("Failed: 0, Passed: 3\nFailed: 0, Passed: 4", 0).Summary.StartsWith("7 passed", StringComparison.Ordinal));

        Check("serialize: LF and sorted", Gate.Serialize(new SortedDictionary<string, string>(StringComparer.Ordinal) { ["b"] = "2", ["a"] = "<1>" }) is var json
            && !json.Contains('\r') && json.IndexOf("\"a\"", StringComparison.Ordinal) < json.IndexOf("\"b\"", StringComparison.Ordinal) && json.Contains("<1>"));

        foreach (var f in failed) Console.WriteLine($"FAIL {f}");
        Console.WriteLine($"selftest: {total - failed.Count}/{total} passed");
        return failed.Count == 0 ? 0 : 1;
    }
}
