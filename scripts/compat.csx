// The compatibility gate (workspace `cmem/layout-and-packaging.md` § "The compatibility gate";
// this repo's `cmem/compat-gate.md`). "Backward compatible" is a gate result, never a judgment
// made at bump time.
//
//   dotnet10 tool run dotnet-script scripts/compat.csx -- surface [--check]   update / verify PublicAPI.*.txt (Rule 1)
//   dotnet10 tool run dotnet-script scripts/compat.csx -- report              floor + previous-tag tests vs the last v* tag
//   dotnet10 tool run dotnet-script scripts/compat.csx -- bump <patch|minor|major>
//   dotnet10 tool run dotnet-script scripts/compat.csx -- release             CI, on the tagged commit
//   dotnet10 tool run dotnet-script scripts/compat.csx -- selftest            pure checks: no git, build or network
//
// Rule 1's surface is declared by Microsoft.CodeAnalysis.PublicApiAnalyzers: PublicAPI.Shipped.txt
// (as of the last release) + PublicAPI.Unshipped.txt (since). The compiler fails the build when the
// code disagrees with them (RS0016/RS0017/RS0036/RS0037 are errors in the csproj), so a stale
// surface cannot be built. This script diffs those lines plus the README's SPEC version.
//
// Child `dotnet` processes use $UWL_DOTNET, else `dotnet10` when it is on PATH, else `dotnet`.
//
// C# script run by dotnet-script, pinned in dotnet-tools.json (owner, 2026-11-08: "move them");
// ported from compat.ts, whose output it reproduces line for line (the workspace port rule).

#nullable enable
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

Console.OutputEncoding = new UTF8Encoding(false);

class GateError : Exception
{
    public GateError(string message) : base(message) { }
}

enum Level { patch = 0, minor = 1, major = 2 }

static Level MaxLevel(Level a, Level b) => a >= b ? a : b;

// A set that keeps insertion order, as a JavaScript Set does.
class OrderedSet
{
    private readonly List<string> _items = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    public OrderedSet(IEnumerable<string>? items = null) { foreach (var i in items ?? Array.Empty<string>()) Add(i); }
    public void Add(string s) { if (_seen.Add(s)) _items.Add(s); }
    public void Delete(string s) { if (_seen.Remove(s)) _items.Remove(s); }
    public bool Has(string s) => _seen.Contains(s);
    public int Size => _items.Count;
    public IReadOnlyList<string> Items => _items;
}

// ---------------------------------------------------------------- SemVer

record SemVer(int Major, int Minor, int Patch);

static SemVer ParseSemVer(string s)
{
    var m = Regex.Match(s.Trim(), @"^v?([0-9]+)\.([0-9]+)\.([0-9]+)$");
    if (!m.Success) throw new GateError($"not a plain SemVer version: '{s}'");
    return new SemVer(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
}

static string Fmt(SemVer v) => $"{v.Major}.{v.Minor}.{v.Patch}";

static int Compare(SemVer a, SemVer b) =>
    a.Major != b.Major ? a.Major - b.Major : a.Minor != b.Minor ? a.Minor - b.Minor : a.Patch - b.Patch;

static SemVer Next(SemVer v, Level kind) => kind switch
{
    Level.major => new SemVer(v.Major + 1, 0, 0),
    Level.minor => new SemVer(v.Major, v.Minor + 1, 0),
    _ => new SemVer(v.Major, v.Minor, v.Patch + 1),
};

// The level a release from `from` to `to` claims; `to` must be greater.
static Level Step(SemVer from, SemVer to)
{
    if (Compare(to, from) <= 0) throw new GateError($"{Fmt(to)} is not above {Fmt(from)}");
    return to.Major > from.Major ? Level.major : to.Minor > from.Minor ? Level.minor : Level.patch;
}

// ---------------------------------------------------------------- Rule 1: the surface

record Surface(string Spec, OrderedSet Symbols);

const string REMOVED = "*REMOVED*";

static List<string> ApiLines(string text) =>
    text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l != "" && !l.StartsWith("#", StringComparison.Ordinal)).ToList();

// The declared API: Shipped plus Unshipped, minus Unshipped's `*REMOVED*` lines (the analyzer's
// convention for removing a shipped symbol).
static OrderedSet DeclaredApi(string shipped, string unshipped)
{
    var api = new OrderedSet(ApiLines(shipped));
    foreach (var line in ApiLines(unshipped))
    {
        if (line.StartsWith(REMOVED, StringComparison.Ordinal)) api.Delete(line[REMOVED.Length..]);
        else api.Add(line);
    }
    return api;
}

// Release: Unshipped folds into Shipped, and Unshipped starts over. Shipped keeps its own order
// (the analyzer's fixer writes it that way); removed lines drop out, new ones are appended, so a
// release with no API change leaves Shipped byte-identical.
static (string Shipped, string Unshipped) FoldUnshipped(string shipped, string unshipped)
{
    var api = DeclaredApi(shipped, unshipped);
    var kept = DeclaredApi(shipped, "").Items.Where(api.Has).ToList();
    var added = api.Items.Where(l => !kept.Contains(l)).ToList();
    return ($"#nullable enable\n{string.Join("\n", kept.Concat(added))}\n", "#nullable enable\n");
}

static string SpecVersion(string readme)
{
    var matches = Regex.Matches(readme, @"\*\*SPEC\.md v([0-9]+\.[0-9]+\.[0-9]+)\*\*");
    if (matches.Count != 1)
        throw new GateError($"README must declare '**SPEC.md vX.Y.Z**' exactly once (found {matches.Count})");
    return matches[0].Groups[1].Value;
}

// ---------------------------------------------------------------- Rule 2: the floor

record Floor(Level Level, List<string> Reasons);

// Removed (or changed: a changed line is removed + added) → major; added → minor; nothing → patch.
// The SPEC version moves it by its own level; lowered → major.
static Floor FloorOf(Surface before, Surface after)
{
    var level = Level.patch;
    var reasons = new List<string>();
    void Raise(Level l, string why)
    {
        level = MaxLevel(level, l);
        reasons.Add(why);
    }
    foreach (var s in before.Symbols.Items.OrderBy(x => x, StringComparer.Ordinal)) if (!after.Symbols.Has(s)) Raise(Level.major, $"removed: {s}");
    foreach (var s in after.Symbols.Items.OrderBy(x => x, StringComparer.Ordinal)) if (!before.Symbols.Has(s)) Raise(Level.minor, $"added: {s}");
    var sb = ParseSemVer(before.Spec);
    var sa = ParseSemVer(after.Spec);
    if (Compare(sa, sb) < 0) Raise(Level.major, $"SPEC lowered: {Fmt(sb)} → {Fmt(sa)}");
    else if (Compare(sa, sb) > 0) Raise(Step(sb, sa), $"SPEC raised: {Fmt(sb)} → {Fmt(sa)}");
    return new Floor(level, reasons);
}

record TestResult(bool Green, string Summary);

// Everything that blocks a release from `last` to `target`. Rule 6: below 1.0.0 a minor is allowed
// where the floor says major, but the Breaking Changes row is still mandatory.
static List<string> Problems(SemVer last, SemVer target, Floor floor, bool breakingRow, TestResult previous)
{
    var output = new List<string>();
    var claimed = Step(last, target);
    var effective = last.Major == 0 && floor.Level == Level.major ? Level.minor : floor.Level;
    if (claimed < effective)
    {
        var note = effective != floor.Level ? ", minor under the 0.x rule" : "";
        output.Add($"{claimed} {Fmt(last)} → {Fmt(target)} is below the floor ({floor.Level}{note})");
    }
    if (floor.Level == Level.major && !breakingRow)
        output.Add("the floor is major: README '### ⚠️ Breaking Changes' needs a row for this release");
    if (floor.Level != Level.major && !previous.Green) output.Add($"previous-tag tests are not green: {previous.Summary}");
    return output;
}

// ---------------------------------------------------------------- README (Rule 4)

const string NEXT_RELEASE = "_next release_";

static (int Start, int End)? BreakingSection(string readme)
{
    var heading = Regex.Match(readme, @"^###\s+⚠️\s*Breaking Changes\s*$", RegexOptions.Multiline);
    if (!heading.Success) return null;
    var start = heading.Index + heading.Length;
    var rest = Regex.Match(readme[start..], @"^#{1,3}\s", RegexOptions.Multiline);
    return (start, rest.Success ? start + rest.Index : readme.Length);
}

static bool HasRow(string readme, string label)
{
    if (BreakingSection(readme) is not var (start, end)) return false;
    return readme[start..end].Split('\n').Any(l => l.TrimStart().StartsWith($"| {label} |", StringComparison.Ordinal));
}

static string StampNextRelease(string readme, SemVer version)
{
    if (BreakingSection(readme) is not var (start, end) || !HasRow(readme, NEXT_RELEASE))
        throw new GateError($"no '| {NEXT_RELEASE} |' row under '### ⚠️ Breaking Changes'");
    var body = readme[start..end].Replace($"| {NEXT_RELEASE} |", $"| {Fmt(version)} |");
    return readme[..start] + body + readme[end..];
}

// ---------------------------------------------------------------- the version line

static SemVer ReadVersion(string csproj)
{
    var matches = Regex.Matches(csproj, "<Version>([^<]*)</Version>");
    if (matches.Count != 1) throw new GateError($"the csproj must hold exactly one <Version> (found {matches.Count})");
    return ParseSemVer(matches[0].Groups[1].Value);
}

static string WriteVersion(string csproj, SemVer version)
{
    ReadVersion(csproj);
    return new Regex("<Version>[^<]*</Version>").Replace(csproj, _ => $"<Version>{Fmt(version)}</Version>", 1);
}

// ---------------------------------------------------------------- test output (workspace 1.1)

// Green needs exit 0, at least one pass and no failures: the OUTPUT counts, not the exit code.
static TestResult JudgeTests(string output, int exitCode)
{
    int passed = 0, failed = 0, runs = 0;
    foreach (Match m in Regex.Matches(output, @"Failed:\s+([0-9]+),\s+Passed:\s+([0-9]+)"))
    {
        failed += int.Parse(m.Groups[1].Value);
        passed += int.Parse(m.Groups[2].Value);
        runs++;
    }
    var summary = runs == 0 ? $"no test counts in the output (exit {exitCode})" : $"{passed} passed, {failed} failed (exit {exitCode})";
    return new TestResult(exitCode == 0 && runs > 0 && passed > 0 && failed == 0, summary);
}

// ---------------------------------------------------------------- processes

// Output = all of stdout, then all of stderr (as Bun.spawnSync gives them).
static (int Code, string Output) Run(string[] cmd, string cwd)
{
    var psi = new ProcessStartInfo(ResolveExe(cmd[0]))
    {
        WorkingDirectory = cwd,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        UseShellExecute = false,
    };
    foreach (var a in cmd.Skip(1)) psi.ArgumentList.Add(a);
    psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
    psi.Environment["DOTNET_NOLOGO"] = "1";
    using var p = Process.Start(psi)!;
    var stderr = p.StandardError.ReadToEndAsync();
    var stdout = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    return (p.ExitCode, stdout + stderr.Result);
}

static string Tail(string text, int n)
{
    var lines = text.TrimEnd().Split('\n');
    return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - n)));
}

static string Must(string[] cmd, string cwd)
{
    var r = Run(cmd, cwd);
    if (r.Code != 0) throw new GateError($"{string.Join(" ", cmd)} failed (exit {r.Code}):\n{Tail(r.Output, 25)}");
    return r.Output;
}

static string Git(string cwd, params string[] args) => Must(new[] { "git" }.Concat(args).ToArray(), cwd).Trim();

// A command name found on PATH (with PATHEXT on Windows), as Bun.which does; null when absent.
static string? Which(string name)
{
    var exts = OperatingSystem.IsWindows()
        ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
        : new[] { "" };
    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var ext in exts)
        {
            var candidate = Path.Combine(dir, name + ext);
            if (File.Exists(candidate)) return candidate;
        }
    return null;
}

static string ResolveExe(string name) => Path.IsPathRooted(name) ? name : Which(name) ?? name;

static string? dotnetExe;
static string Dotnet()
{
    var fromEnv = Environment.GetEnvironmentVariable("UWL_DOTNET");
    dotnetExe ??= !string.IsNullOrEmpty(fromEnv) ? fromEnv : Which("dotnet10") is not null ? "dotnet10" : "dotnet";
    return dotnetExe;
}

// Node's path.join: joined and normalized for the platform.
static string Join(params string[] parts) => Path.GetFullPath(Path.Combine(parts));

// ---------------------------------------------------------------- the gate

const string PROJECT = "UniversalWasmLoader.csproj";
const string SHIPPED = "PublicAPI.Shipped.txt";
const string UNSHIPPED = "PublicAPI.Unshipped.txt";

// Releases published before this gate existed (reviewed exceptions, 2026-10-22). v0.1.0 = commit
// d990087: its tree has no tests/ (gitignored then) and no PublicAPI files. Its surface is the
// first committed PublicAPI.Shipped.txt, generated from library sources byte-identical to v0.1.0's
// (`git diff d990087 <that commit> -- *.cs` at the root is empty; cmem/compat-gate.md).
static readonly HashSet<string> UNGATED = new() { "v0.1.0" };

static string RepoRoot() => Git(Directory.GetCurrentDirectory(), "rev-parse", "--show-toplevel");
static string Read(string path) => File.ReadAllText(path);

static Surface SurfaceOfTree(string dir) =>
    new(SpecVersion(Read(Join(dir, "README.md"))), DeclaredApi(Read(Join(dir, SHIPPED)), Read(Join(dir, UNSHIPPED))));

static Surface BaselineSurface(string repo, string tag, string tree)
{
    if (File.Exists(Join(tree, SHIPPED))) return SurfaceOfTree(tree);
    if (!UNGATED.Contains(tag)) throw new GateError($"{tag} has no {SHIPPED}: no baseline surface");
    var first = Git(repo, "log", "--diff-filter=A", "--format=%H", "--", SHIPPED).Split('\n').Where(s => s != "").LastOrDefault();
    if (first is null) throw new GateError($"no commit adds {SHIPPED}: cannot stand in for {tag}");
    return new Surface(SpecVersion(Read(Join(tree, "README.md"))), DeclaredApi(Git(repo, "show", $"{first}:{SHIPPED}"), ""));
}

static string? LastTag(string repo, bool excludeHead)
{
    List<string> Lines(string s) => s.Split('\n').Select(t => t.Trim()).Where(t => t != "").ToList();
    var atHead = new HashSet<string>(excludeHead ? Lines(Git(repo, "tag", "--points-at", "HEAD", "--list", "v*")) : new List<string>());
    return Lines(Git(repo, "tag", "--merged", "HEAD", "--list", "v*"))
        .Where(t => !atHead.Contains(t) && Regex.IsMatch(t, @"^v[0-9]+\.[0-9]+\.[0-9]+$"))
        .OrderByDescending(t => ParseSemVer(t), Comparer<SemVer>.Create(Compare))
        .FirstOrDefault();
}

// The PublicAPI analyzer's errors in build output, one per symbol: the message without MSBuild's
// trailing help link and `[project::TargetFramework=…]` (a symbol may itself contain `[]`).
static List<string> AnalyzerFindings(string output)
{
    var found = new OrderedSet();
    foreach (var line in Regex.Split(output, "\r?\n"))
    {
        var m = Regex.Match(line, @"error (RS00(?:16|17|36|37)): (.*)$");
        if (!m.Success) continue;
        var message = Regex.Replace(m.Groups[2].Value, @"\s*\[[^\]]*::TargetFramework=[^\]]*\]\s*$", "");
        message = Regex.Replace(message, @"\s*\(https?://[^)]*\)\s*$", "");
        found.Add($"{m.Groups[1].Value}: {message.Trim()}");
    }
    return found.Items.ToList();
}

// Rule 1 enforcement: the build fails on any RS0016/RS0017/RS0036/RS0037 (csproj WarningsAsErrors).
// `--no-incremental`: the analyzer only reports when the compiler runs, and on exFAT (`D:`) an
// incremental build has been seen to reuse stale output after a source edit (2026-10-22).
static (bool Ok, List<string> Findings) BuildCheck(string repo)
{
    var r = Run(new[] { Dotnet(), "build", Join(repo, PROJECT), "-c", "Release", "--no-incremental" }, repo);
    var findings = AnalyzerFindings(r.Output);
    if (r.Code != 0 && findings.Count == 0) throw new GateError($"the build failed for another reason:\n{Tail(r.Output, 25)}");
    return (r.Code == 0, findings);
}

static void CopyTree(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    foreach (var d in Directory.GetDirectories(from))
    {
        var name = Path.GetFileName(d);
        if (name == "bin" || name == "obj") continue;
        CopyTree(d, Path.Combine(to, name));
    }
}

// Rule 3: the previous tag's tests/ (test code AND fixtures, unmodified) against the CURRENT library.
static TestResult PreviousTests(string repo, string tree, string tag)
{
    var tests = Join(tree, "tests");
    if (!Directory.Exists(tests))
    {
        return UNGATED.Contains(tag)
            ? new TestResult(true, $"exempt: {tag} predates the gate and its commit has no tests/ (reviewed exception)")
            : new TestResult(false, $"{tag} has no tests/ to run");
    }
    var work = Directory.CreateTempSubdirectory("uwl-prevtests-").FullName;
    try
    {
        var copy = Join(work, "tests");
        CopyTree(tests, copy);
        var csprojName = Directory.GetFiles(copy, "*.csproj").Select(Path.GetFileName).ToList();
        if (csprojName.Count != 1) throw new GateError($"{tag}'s tests/ must hold exactly one .csproj");
        var csprojPath = Join(copy, csprojName[0]!);
        var text = Read(csprojPath);
        var reference = new Regex("Include=\"[^\"]*UniversalWasmLoader\\.csproj\"");
        if (reference.Matches(text).Count != 1)
            throw new GateError($"{tag}'s test project must reference UniversalWasmLoader.csproj exactly once");
        File.WriteAllText(csprojPath, reference.Replace(text, _ => $"Include=\"{Join(repo, PROJECT)}\""));
        var r = Run(new[] { Dotnet(), "test", copy }, work);
        var result = JudgeTests(r.Output, r.Code);
        if (!result.Green) Console.WriteLine(Tail(r.Output, 15));
        return result;
    }
    finally
    {
        Directory.Delete(work, recursive: true);
    }
}

record Evaluation(string Tag, Floor Floor, TestResult Previous, List<string> Stale);

static Evaluation Evaluate(string repo, bool excludeHead)
{
    var tag = LastTag(repo, excludeHead) ?? throw new GateError("no v* tag is reachable from HEAD: nothing to compare against");
    var check = BuildCheck(repo);
    var stale = check.Ok ? new List<string>() : check.Findings.Select(f => $"the PublicAPI files disagree with the code: {f}").ToList();
    var current = SurfaceOfTree(repo);

    var tree = Directory.CreateTempSubdirectory("uwl-tag-").FullName;
    Directory.Delete(tree, recursive: true);
    Git(repo, "worktree", "add", "--detach", tree, tag);
    try
    {
        var floor = FloorOf(BaselineSurface(repo, tag, tree), current);
        Console.WriteLine($"last tag {tag}; floor {floor.Level}{(floor.Reasons.Count == 0 ? " (surface unchanged)" : ":")}");
        foreach (var r in floor.Reasons) Console.WriteLine($"  {r}");
        var previous = PreviousTests(repo, tree, tag);
        Console.WriteLine($"previous-tag tests ({tag}'s tests/ against the current source): {(previous.Green ? "GREEN" : "RED")}, {previous.Summary}");
        return new Evaluation(tag, floor, previous, stale);
    }
    finally
    {
        Run(new[] { "git", "worktree", "remove", "--force", tree }, repo);
        if (Directory.Exists(tree)) Directory.Delete(tree, recursive: true);
    }
}

static int CmdSurface(bool check)
{
    var repo = RepoRoot();
    if (!check)
    {
        // Adds new public symbols to Unshipped; a removed symbol is reported by the build (RS0017) and
        // its line is deleted (or marked *REMOVED* in Unshipped when it had shipped) by hand.
        Must(new[] { Dotnet(), "format", "analyzers", Join(repo, PROJECT), "--diagnostics", "RS0016", "RS0037", "--severity", "info" }, repo);
    }
    var r = BuildCheck(repo);
    if (r.Ok)
    {
        Console.WriteLine($"{SHIPPED} + {UNSHIPPED} match the code ({SurfaceOfTree(repo).Symbols.Size} symbols, SPEC {SurfaceOfTree(repo).Spec})");
        return 0;
    }
    Console.WriteLine("STALE: the PublicAPI files disagree with the code (Rule 5: re-baseline in its own commit):");
    foreach (var f in r.Findings) Console.WriteLine($"  {f}");
    return 1;
}

static int CmdReport()
{
    var e = Evaluate(RepoRoot(), false);
    foreach (var s in e.Stale) Console.WriteLine($"  {s}");
    return e.Stale.Count == 0 ? 0 : 1;
}

static int CmdBump(string? kindArg)
{
    if (kindArg != "patch" && kindArg != "minor" && kindArg != "major") throw new GateError("bump needs patch, minor or major");
    var kind = Enum.Parse<Level>(kindArg);
    var repo = RepoRoot();
    if (Git(repo, "status", "--porcelain") != "") throw new GateError("the working tree is not clean");

    var csprojPath = Join(repo, PROJECT);
    var csproj = Read(csprojPath);
    var line = ReadVersion(csproj);
    var e = Evaluate(repo, false);
    var tagVersion = ParseSemVer(e.Tag);
    var target = Next(tagVersion, kind);
    var readmePath = Join(repo, "README.md");
    var readme = Read(readmePath);
    var found = new List<string>(e.Stale);
    if (Compare(line, tagVersion) != 0) found.Add($"the version line ({Fmt(line)}) must equal the last tag ({e.Tag}) before a bump");
    found.AddRange(Problems(tagVersion, target, e.Floor, HasRow(readme, NEXT_RELEASE), e.Previous));

    if (found.Count > 0)
    {
        Console.WriteLine($"REFUSED: bump {kindArg} {Fmt(tagVersion)} → {Fmt(target)}");
        foreach (var p in found) Console.WriteLine($"  {p}");
        return 1;
    }
    File.WriteAllText(csprojPath, WriteVersion(csproj, target));
    var folded = FoldUnshipped(Read(Join(repo, SHIPPED)), Read(Join(repo, UNSHIPPED)));
    File.WriteAllText(Join(repo, SHIPPED), folded.Shipped);
    File.WriteAllText(Join(repo, UNSHIPPED), folded.Unshipped);
    if (e.Floor.Level == Level.major) File.WriteAllText(readmePath, StampNextRelease(readme, target));
    Console.WriteLine($"bumped {Fmt(tagVersion)} → {Fmt(target)} (floor {e.Floor.Level}); {UNSHIPPED} folded into {SHIPPED}.");
    Console.WriteLine($"Review, commit, then tag v{Fmt(target)}; nothing was committed, tagged or pushed.");
    return 0;
}

static int CmdRelease()
{
    var repo = RepoRoot();
    var line = ReadVersion(Read(Join(repo, PROJECT)));
    var headTags = Git(repo, "tag", "--points-at", "HEAD", "--list", "v*").Split('\n').Where(s => s != "").ToList();
    if (!headTags.Contains($"v{Fmt(line)}"))
        throw new GateError($"HEAD is not tagged v{Fmt(line)} (the version line); tags here: {(headTags.Count > 0 ? string.Join(", ", headTags) : "none")}");
    var e = Evaluate(repo, true);
    var found = e.Stale.Concat(Problems(ParseSemVer(e.Tag), line, e.Floor, HasRow(Read(Join(repo, "README.md")), Fmt(line)), e.Previous)).ToList();
    if (found.Count == 0)
    {
        Console.WriteLine($"release v{Fmt(line)} passes the gate");
        return 0;
    }
    Console.WriteLine($"BLOCKED: release v{Fmt(line)}");
    foreach (var p in found) Console.WriteLine($"  {p}");
    return 1;
}

// ---------------------------------------------------------------- self-test

static int Selftest()
{
    var failed = new List<string>();
    var total = 0;
    void Check(string name, bool ok)
    {
        total++;
        if (!ok) failed.Add(name);
    }
    bool Throws(Action f)
    {
        try { f(); return false; }
        catch (GateError) { return true; }
        catch { return false; }
    }

    Surface S(string spec, params string[] symbols) => new(spec, new OrderedSet(symbols));
    var b = S("3.0.0", "A", "A.F() -> int");
    Check("floor: identical → patch", FloorOf(b, b).Level == Level.patch && FloorOf(b, b).Reasons.Count == 0);
    Check("floor: symbol added → minor", FloorOf(b, S("3.0.0", "A", "A.F() -> int", "A.G() -> void")).Level == Level.minor);
    Check("floor: symbol removed → major", FloorOf(b, S("3.0.0", "A")).Level == Level.major);
    Check("floor: signature changed → major", FloorOf(b, S("3.0.0", "A", "A.F() -> long")).Level == Level.major);
    Check("floor: SPEC patch raise → patch", FloorOf(b, S("3.0.1", "A", "A.F() -> int")).Level == Level.patch);
    Check("floor: SPEC minor raise → minor", FloorOf(b, S("3.1.0", "A", "A.F() -> int")).Level == Level.minor);
    Check("floor: SPEC major raise → major", FloorOf(b, S("4.0.0", "A", "A.F() -> int")).Level == Level.major);
    Check("floor: SPEC lowered → major", FloorOf(b, S("2.9.9", "A", "A.F() -> int")).Level == Level.major);
    Check("floor: reasons name the symbol", FloorOf(b, S("3.0.0", "A")).Reasons.Contains("removed: A.F() -> int"));

    SemVer V(string s) => ParseSemVer(s);
    Check("semver: parse with v", Compare(V("v1.2.3"), new SemVer(1, 2, 3)) == 0);
    Check("semver: prerelease rejected", Throws(() => V("1.2.3-rc.1")));
    Check("semver: 1.10.0 > 1.9.9", Compare(V("1.10.0"), V("1.9.9")) > 0);
    Check("semver: next patch", Fmt(Next(V("0.1.0"), Level.patch)) == "0.1.1");
    Check("semver: next minor resets patch", Fmt(Next(V("0.1.4"), Level.minor)) == "0.2.0");
    Check("semver: next major resets both", Fmt(Next(V("1.4.4"), Level.major)) == "2.0.0");
    Check("semver: step minor", Step(V("0.1.0"), V("0.2.0")) == Level.minor);
    Check("semver: step not increasing throws", Throws(() => Step(V("0.2.0"), V("0.2.0"))));

    var green = new TestResult(true, "ok");
    var red = new TestResult(false, "1 failed");
    Floor F(Level level) => new(level, new List<string>());
    Check("policy: patch at floor patch allowed", Problems(V("1.0.0"), V("1.0.1"), F(Level.patch), false, green).Count == 0);
    Check("policy: patch at floor minor refused", Problems(V("1.0.0"), V("1.0.1"), F(Level.minor), false, green).Count == 1);
    Check("policy: minor at floor minor allowed", Problems(V("1.0.0"), V("1.1.0"), F(Level.minor), false, green).Count == 0);
    Check("policy: 1.x minor at floor major refused", Problems(V("1.0.0"), V("1.1.0"), F(Level.major), true, green).Count == 1);
    Check("policy: 0.x minor at floor major + row allowed", Problems(V("0.1.0"), V("0.2.0"), F(Level.major), true, green).Count == 0);
    Check("policy: 0.x minor at floor major, no row refused", Problems(V("0.1.0"), V("0.2.0"), F(Level.major), false, green).Count == 1);
    Check("policy: 0.x patch at floor major refused", Problems(V("0.1.0"), V("0.1.1"), F(Level.major), true, green).Count == 1);
    Check("policy: major at floor major + row allowed", Problems(V("1.0.0"), V("2.0.0"), F(Level.major), true, green).Count == 0);
    Check("policy: major at floor major, no row refused", Problems(V("1.0.0"), V("2.0.0"), F(Level.major), false, green).Count == 1);
    Check("policy: red previous tests block a patch", Problems(V("1.0.0"), V("1.0.1"), F(Level.patch), false, red).Count == 1);
    Check("policy: red previous tests do not block at floor major", Problems(V("1.0.0"), V("2.0.0"), F(Level.major), true, red).Count == 0);
    Check("policy: above the floor is allowed", Problems(V("1.0.0"), V("1.1.0"), F(Level.patch), false, green).Count == 0);

    var shipped = "#nullable enable\nA\nA.F() -> int\n";
    Check("api: shipped lines, header skipped", string.Join("|", DeclaredApi(shipped, "#nullable enable\n").Items) == "A|A.F() -> int");
    Check("api: unshipped adds", DeclaredApi(shipped, "#nullable enable\nA.G() -> void\n").Has("A.G() -> void"));
    Check("api: *REMOVED* removes", !DeclaredApi(shipped, "#nullable enable\n*REMOVED*A.F() -> int\n").Has("A.F() -> int"));
    Check("api: CRLF tolerated", DeclaredApi("#nullable enable\r\nA\r\n", "").Has("A"));
    var folded = FoldUnshipped(shipped, "#nullable enable\nA.G() -> void\n*REMOVED*A.F() -> int\n");
    Check("api: fold moves adds, applies removes", folded.Shipped == "#nullable enable\nA\nA.G() -> void\n" && folded.Unshipped == "#nullable enable\n");
    var unsorted = "#nullable enable\nstatic Z.M() -> void\nA\n";
    Check("api: an empty fold leaves Shipped byte-identical", FoldUnshipped(unsorted, "#nullable enable\n").Shipped == unsorted);
    Check("api: fold appends after the existing order", FoldUnshipped(unsorted, "#nullable enable\nB\n").Shipped == $"{unsorted}B\n");

    var readme =
        "# X\n\n- Conforms to **SPEC.md v3.0.0** (ABI)\n\n### ⚠️ Breaking Changes\n\n| Version | Change |\n| --- | --- |\n" +
        "| _next release_ | removed `F` costs $& and $$ |\n| 0.2.0 | renamed `G` |\n\n## License\n\n| _next release_ | decoy |\n";
    Check("readme: spec read", SpecVersion(readme) == "3.0.0");
    Check("readme: spec twice throws", Throws(() => SpecVersion($"{readme}\n**SPEC.md v3.0.0**\n")));
    Check("readme: spec absent throws", Throws(() => SpecVersion("# X\n")));
    Check("readme: next-release row found", HasRow(readme, NEXT_RELEASE));
    Check("readme: version row found", HasRow(readme, "0.2.0"));
    Check("readme: missing version row", !HasRow(readme, "0.3.0"));
    Check("readme: row outside the section ignored", !HasRow(readme.Replace("| _next release_ | removed", "| x | removed"), NEXT_RELEASE));
    Check("readme: no heading → no row", !HasRow("# X\n| _next release_ | y |\n", NEXT_RELEASE));
    var stamped = StampNextRelease(readme, V("0.3.0"));
    Check("readme: stamp writes the version", HasRow(stamped, "0.3.0") && !HasRow(stamped, NEXT_RELEASE));
    Check("readme: stamp leaves other sections alone", stamped.Contains("| _next release_ | decoy |"));
    Check("readme: stamp keeps $ sequences literal", stamped.Contains("costs $& and $$"));
    Check("readme: stamp without a row throws", Throws(() => StampNextRelease(stamped, V("0.4.0"))));

    var csproj = "<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>";
    Check("version line: read", Fmt(ReadVersion(csproj)) == "0.1.0");
    Check("version line: write", Fmt(ReadVersion(WriteVersion(csproj, V("0.2.0")))) == "0.2.0");
    Check("version line: two throw", Throws(() => ReadVersion($"{csproj}<Version>1.0.0</Version>")));

    Check("tests: green output", JudgeTests("Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7", 0).Green);
    Check("tests: a failure is red", !JudgeTests("Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7", 1).Green);
    Check("tests: exit 0 with a failure is red", !JudgeTests("Failed!  - Failed:     1, Passed:     6, Skipped:     0, Total:     7", 0).Green);
    Check("tests: zero passed is red", !JudgeTests("Passed!  - Failed:     0, Passed:     0, Skipped:     0, Total:     0", 0).Green);
    Check("tests: no counts is red", !JudgeTests("Build FAILED.", 0).Green);
    Check("tests: counts summed over runs", JudgeTests("Failed: 0, Passed: 3\nFailed: 0, Passed: 4", 0).Summary.StartsWith("7 passed", StringComparison.Ordinal));

    var rs =
        "src\\WasmModule.cs(70,5): error RS0017: Symbol 'UniversalWasmLoader.WasmModule.Bind(string! name) -> System.Func<object?[]!, object?>!' is part of the declared API, but is either not public or could not be found (https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/PublicApiAnalyzers/PublicApiAnalyzers.Help.md) [src\\UniversalWasmLoader.csproj::TargetFramework=net8.0]";
    var findings = AnalyzerFindings($"{rs}\r\n{rs.Replace("net8.0", "net10.0")}\nerror CS0103: unrelated");
    Check(
        "analyzer: full symbol kept through []",
        findings.Count == 1 && findings[0] == "RS0017: Symbol 'UniversalWasmLoader.WasmModule.Bind(string! name) -> System.Func<object?[]!, object?>!' is part of the declared API, but is either not public or could not be found");
    Check("analyzer: other errors ignored", AnalyzerFindings("x.cs(1,1): error CS0103: The name 'y' does not exist").Count == 0);

    foreach (var f in failed) Console.WriteLine($"FAIL {f}");
    Console.WriteLine($"selftest: {total - failed.Count}/{total} passed");
    return failed.Count == 0 ? 0 : 1;
}

// ---------------------------------------------------------------- main

var command = Args.Count > 0 ? Args[0] : "report";
var arg = Args.Count > 1 ? Args[1] : null;
try
{
    var code =
        command == "surface" ? CmdSurface(arg == "--check")
        : command == "report" ? CmdReport()
        : command == "bump" ? CmdBump(arg)
        : command == "release" ? CmdRelease()
        : command == "selftest" ? Selftest()
        : Usage();
    Environment.Exit(code);
}
catch (GateError e)
{
    Console.Error.WriteLine($"compat: {e.Message}");
    Environment.Exit(2);
}

static int Usage()
{
    Console.Error.WriteLine("usage: compat.csx <surface [--check] | report | bump <patch|minor|major> | release | selftest>");
    return 2;
}
