// The test suite must run on EVERY currently supported .NET line, LTS and STS (owner, 2026-11-07:
// "like the -js version, does this project cover all of the current dotnet lts and sts versions
// available?" → "yes"). Modelled on loader-js's `scripts/check-lts.ts`.
//
// Which lines: Microsoft's own release index (`releases-index.json`), every channel whose support
// phase is `active` or `maintenance` and whose end-of-support date has not passed. That is decided
// by the date, never by a list here, so a line ENTERS this check on its GA (its phase becomes
// `active`; a `go-live` release candidate is not yet required) and LEAVES it once it is within
// LEAVING_DAYS (30) of its end-of-support date: a leaving line is reported, not required (owner,
// 2026-11-07: "I think we need to move to dotnet 10 since the EOL for 8 and 9 are this coming
// week" — the package is .NET 10 only from 0.2.0).
//
// What it checks: each supported line is (1) a target of the test project
// (`tests/UniversalWasmLoader.Tests.csproj` `<TargetFrameworks>`, as `net<major>.0`) and (2) an SDK
// that EVERY CI job running `dotnet test` installs (in each `.github/workflows/*.yml`, that job's
// `actions/setup-dotnet` `dotnet-version`, as `<major>.0.x`); a job that never runs the tests
// (e.g. the push-only `publish` job) is not checked. A missing line fails, naming the file and job.
// A target that is no longer supported is reported, not failed: the package may still ship it.
// So is a target AHEAD of its GA (a go-live / preview line, e.g. net11.0 on the 11 RC; the test
// project adds it only when built with SDK 11+). Every `<TargetFramework(s)>` element counts,
// conditional ones included.
//
//   dotnet10 tool run dotnet-script scripts/runtimes.csx              the check (fetches the index)
//   dotnet10 tool run dotnet-script scripts/runtimes.csx -- selftest  pure-function checks, no network
//
// C# script run by dotnet-script, pinned in dotnet-tools.json (owner, 2026-11-08: "move them");
// ported from runtimes.ts, whose output it reproduces line for line (the workspace port rule).

#r "nuget: YamlDotNet, 18.1.0"
#nullable enable

using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

static string ScriptDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

const string INDEX = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";
static readonly string ROOT = Path.GetFullPath(Path.Combine(ScriptDir(), ".."));
const string TEST_PROJECT = "tests/UniversalWasmLoader.Tests.csproj";
const string WORKFLOWS = ".github/workflows";

/// A supported line this close to its end of support is "leaving": reported, not required.
const int LEAVING_DAYS = 30;

/// One channel of Microsoft's release index (only the fields used here).
record Channel(string ChannelVersion, string ReleaseType, string SupportPhase, string? EolDate = null);

record Line(int Major, string Type, string Eol);

/// Whether a line with end-of-support date `eol` (or "none") is leaving on `today`.
static bool IsLeaving(string eol, string today)
{
    if (eol == "none") return false;
    var days = (DateTime.Parse($"{eol}T00:00:00Z").ToUniversalTime() - DateTime.Parse($"{today}T00:00:00Z").ToUniversalTime()).TotalDays;
    return days <= LEAVING_DAYS;
}

/// The supported channels on `today` (YYYY-MM-DD): phase active / maintenance, EOL not passed.
static List<Line> SupportedLines(IEnumerable<Channel> channels, string today) =>
    channels
        .Where(c => c.SupportPhase == "active" || c.SupportPhase == "maintenance")
        .Where(c => string.IsNullOrEmpty(c.EolDate) || string.CompareOrdinal(today, c.EolDate) <= 0)
        .Select(c => new Line(MajorOf(c.ChannelVersion), c.ReleaseType, string.IsNullOrEmpty(c.EolDate) ? "none" : c.EolDate!))
        .OrderBy(l => l.Major)
        .ToList();

static int MajorOf(string version)
{
    var m = Regex.Match(version.Trim(), @"^([0-9]+)\.[0-9]+$");
    if (!m.Success) throw new Exception($"unexpected channel-version \"{version}\"");
    return int.Parse(m.Groups[1].Value);
}

/// The channels not yet released (phase go-live / preview): a target for one is "ahead of GA".
static List<int> UpcomingLines(IEnumerable<Channel> channels) =>
    channels.Where(c => c.SupportPhase == "go-live" || c.SupportPhase == "preview")
        .Select(c => MajorOf(c.ChannelVersion)).OrderBy(m => m).ToList();

/// The `net<N>.0` majors in a csproj's `<TargetFramework(s)>` elements: ALL of them, conditional
/// ones included (the test project adds net11.0 only on SDK 11+, ahead of 11's GA), in order,
/// each major once.
static List<int> TestTargets(string csproj)
{
    var matches = Regex.Matches(csproj, @"<TargetFrameworks?(?:\s[^>]*)?>([^<]*)</TargetFrameworks?>");
    if (matches.Count == 0) throw new Exception($"{TEST_PROJECT} has no <TargetFramework(s)>");
    var majors = new List<int>();
    foreach (Match m in matches)
        foreach (var t in m.Groups[1].Value.Split(';').Select(t => t.Trim()).Where(t => t != ""))
        {
            var n = Regex.Match(t, @"^net([0-9]+)\.0$");
            if (!n.Success) throw new Exception($"{TEST_PROJECT}: unexpected target \"{t}\"");
            var major = int.Parse(n.Groups[1].Value);
            if (!majors.Contains(major)) majors.Add(major);
        }
    return majors;
}

/// One CI job that runs `dotnet test`, with the `<N>.0.x` majors its `setup-dotnet` installs.
record TestJob(string Where, List<int> Sdks);

static YamlNode? Child(YamlNode? node, string key) =>
    node is YamlMappingNode map && map.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v : null;

static string Scalar(YamlNode? node) => node is YamlScalarNode s ? s.Value ?? "" : "";

/// The jobs of a workflow that run `dotnet test`, each with the SDK majors it installs.
static List<TestJob> TestJobs(string workflowYaml, string file)
{
    var stream = new YamlStream();
    stream.Load(new StringReader(workflowYaml));
    var root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;
    var outJobs = new List<TestJob>();
    if (Child(root, "jobs") is not YamlMappingNode jobs) return outJobs;
    foreach (var (nameNode, job) in jobs.Children)
    {
        var name = Scalar(nameNode);
        var steps = Child(job, "steps") is YamlSequenceNode seq ? seq.Children.ToList() : new List<YamlNode>();
        if (!steps.Any(s => Regex.IsMatch(Scalar(Child(s, "run")), @"\bdotnet\s+test\b"))) continue;
        var sdks = new List<int>();
        foreach (var step in steps)
        {
            if (!Scalar(Child(step, "uses")).StartsWith("actions/setup-dotnet@", StringComparison.Ordinal)) continue;
            foreach (var v in Regex.Split(Scalar(Child(Child(step, "with"), "dotnet-version")), @"\s+").Where(x => x != ""))
            {
                var n = Regex.Match(v, @"^([0-9]+)\.0\.x$");
                if (!n.Success) throw new Exception($"{file} job {name}: unexpected dotnet-version \"{v}\"");
                sdks.Add(int.Parse(n.Groups[1].Value));
            }
        }
        outJobs.Add(new TestJob($"{file} job {name}", sdks));
    }
    return outJobs;
}

/// The problems (empty = pass) and the notes for one set of inputs.
static (List<string> Problems, List<string> Notes) Verdict(List<Line> supported, List<int> targets, List<TestJob> jobs, string today = "1970-01-01", List<int>? upcoming = null)
{
    var problems = new List<string>();
    var notes = new List<string>();
    if (supported.Count == 0) problems.Add("the release index lists no supported .NET line (index format changed?)");
    if (jobs.Count == 0) problems.Add($"no CI job runs `dotnet test` (in {WORKFLOWS})");
    var required = supported.Where(s => !IsLeaving(s.Eol, today)).ToList();
    foreach (var s in supported)
        if (!required.Contains(s)) notes.Add($".NET {s.Major}.0 is leaving (end of support {s.Eol}): not required");
    if (supported.Count > 0 && required.Count == 0) problems.Add("every supported .NET line is leaving: none would be tested");
    foreach (var s in required)
    {
        if (!targets.Contains(s.Major)) problems.Add($".NET {s.Major}.0 is supported but not a test target: add net{s.Major}.0 to {TEST_PROJECT}");
        foreach (var job in jobs)
            if (!job.Sdks.Contains(s.Major)) problems.Add($".NET {s.Major}.0 is supported but {job.Where} does not install it: add {s.Major}.0.x there");
    }
    foreach (var t in targets)
        if (!supported.Any(s => s.Major == t))
            notes.Add(upcoming is not null && upcoming.Contains(t)
                ? $"net{t}.0 is a test target ahead of its GA ({t}.0 is not released yet): not required"
                : $"net{t}.0 is still a test target but no longer supported (kept while the package ships that asset?)");
    return (problems, notes);
}

static async Task<int> Check()
{
    using var http = new HttpClient();
    using var res = await http.GetAsync(INDEX);
    if (!res.IsSuccessStatusCode) throw new Exception($"HTTP {(int)res.StatusCode} for {INDEX}");
    var index = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("releases-index");
    string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    var channels = index.EnumerateArray()
        .Select(c => new Channel(Str(c, "channel-version") ?? "", Str(c, "release-type") ?? "", Str(c, "support-phase") ?? "", Str(c, "eol-date")))
        .ToList();
    var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var supported = SupportedLines(channels, today);
    var targets = TestTargets(File.ReadAllText(Path.Combine(ROOT, TEST_PROJECT)));
    var jobs = Directory.GetFiles(Path.Combine(ROOT, WORKFLOWS))
        .Select(Path.GetFileName)
        .Where(f => Regex.IsMatch(f!, @"\.ya?ml$"))
        .OrderBy(f => f, StringComparer.Ordinal)
        .SelectMany(f => TestJobs(File.ReadAllText(Path.Combine(ROOT, WORKFLOWS, f!)), $"{WORKFLOWS}/{f}"))
        .ToList();
    Console.WriteLine($"supported on {today}: {string.Join(", ", supported.Select(s => $"{s.Major}.0 {s.Type.ToUpperInvariant()} (EOL {s.Eol})"))}");
    Console.WriteLine($"test targets: {string.Join(", ", targets.Select(t => $"net{t}.0"))}");
    foreach (var j in jobs) Console.WriteLine($"{j.Where} (runs dotnet test): SDKs {string.Join(", ", j.Sdks.Select(s => $"{s}.0.x"))}");
    var (problems, notes) = Verdict(supported, targets, jobs, today, UpcomingLines(channels));
    foreach (var n in notes) Console.WriteLine($"note: {n}");
    foreach (var p in problems) Console.Error.WriteLine($"FAIL: {p}");
    if (problems.Count == 0) Console.WriteLine("every supported .NET line is a test target and installed in CI");
    return problems.Count == 0 ? 0 : 1;
}

static int Selftest()
{
    int passed = 0, failed = 0;
    void Ok(string name, bool cond)
    {
        if (cond) passed++;
        else
        {
            failed++;
            Console.Error.WriteLine($"selftest FAILED: {name}");
        }
    }
    bool Throws(Action f)
    {
        try { f(); return false; } catch { return true; }
    }
    var index = new List<Channel>
    {
        new("11.0", "sts", "go-live"),
        new("10.0", "lts", "active", "2028-11-14"),
        new("9.0", "sts", "maintenance", "2026-11-10"),
        new("8.0", "lts", "maintenance", "2026-11-10"),
        new("7.0", "sts", "eol", "2024-05-14"),
    };
    string Majors(string d) => string.Join(",", SupportedLines(index, d).Select(s => s.Major));
    Ok("lines on 2026-11-07", Majors("2026-11-07") == "8,9,10");
    Ok("the EOL day itself is still supported", Majors("2026-11-10") == "8,9,10");
    Ok("lines leave the day after EOL", Majors("2026-11-11") == "10");
    Ok("go-live (RC) is not required", !Majors("2026-11-07").Contains("11"));
    Ok("GA enters", SupportedLines(new[] { index[0] with { SupportPhase = "active" } }, "2026-11-20").Count == 1);
    Ok("bad channel-version throws", Throws(() => SupportedLines(new[] { index[1] with { ChannelVersion = "ten" } }, "2026-11-07")));

    Ok("csproj one target", string.Join(",", TestTargets("<TargetFramework>net10.0</TargetFramework>")) == "10");
    Ok("csproj many targets", string.Join(",", TestTargets("<TargetFrameworks>net8.0; net9.0;net10.0</TargetFrameworks>")) == "8,9,10");
    Ok("csproj odd target throws", Throws(() => TestTargets("<TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>")));
    Ok("csproj without targets throws", Throws(() => TestTargets("<Project/>")));

    var yaml =
        "jobs:\n  v:\n    steps:\n      - uses: actions/checkout@abc\n      - uses: actions/setup-dotnet@def # v4\n        with:\n          dotnet-version: |\n            8.0.x\n            10.0.x\n      - run: dotnet test tests\n" +
        "  push:\n    steps:\n      - uses: actions/setup-dotnet@def\n        with:\n          dotnet-version: 10.0.x\n      - run: dotnet nuget push x\n";
    var jobs = TestJobs(yaml, "wf.yml");
    Ok("only the job that runs dotnet test", jobs.Count == 1 && jobs[0].Where == "wf.yml job v");
    Ok("its sdks", string.Join(",", jobs[0].Sdks) == "8,10");
    Ok("ci odd sdk throws", Throws(() => TestJobs(yaml.Replace("8.0.x", "8.x"), "wf.yml")));

    var sup = new List<Line> { new(8, "", "none"), new(9, "", "none"), new(10, "", "none") };
    TestJob Job(string where, params int[] sdks) => new(where, sdks.ToList());
    Ok("all covered passes", Verdict(sup, new() { 8, 9, 10 }, new() { Job("a", 8, 9, 10), Job("b", 8, 9, 10) }).Problems.Count == 0);
    var missing = Verdict(sup, new() { 8, 10 }, new() { Job("a", 8, 10) }).Problems;
    Ok("a missing line fails twice, naming the csproj and the job", missing.Count == 2 && missing[0].Contains(TEST_PROJECT) && missing[1].Contains("a does not"));
    var oneJob = Verdict(sup, new() { 8, 9, 10 }, new() { Job("a", 8, 9, 10), Job("b", 8, 10) }).Problems;
    Ok("a second test job missing a line fails, naming it", oneJob.Count == 1 && oneJob[0].Contains("b does not"));
    var extra = Verdict(new() { new(10, "", "none") }, new() { 8, 10 }, new() { Job("a", 8, 10) });
    Ok("an unsupported target is a note, not a failure", extra.Problems.Count == 0 && extra.Notes.Count == 1);
    Ok("an empty index fails", Verdict(new(), new() { 10 }, new() { Job("a", 10) }).Problems.Count == 1);
    Ok("no test job fails", Verdict(new() { new(10, "", "none") }, new() { 10 }, new()).Problems.Count == 1);

    var lines = new List<Line> { new(8, "", "2026-11-10"), new(9, "", "2026-11-10"), new(10, "", "2028-11-14") };
    var only10 = Verdict(lines, new() { 10 }, new() { Job("a", 10) }, "2026-11-07");
    Ok("lines within 30 days of EOL are leaving, not required", only10.Problems.Count == 0 && only10.Notes.Count(n => n.Contains("leaving")) == 2);
    Ok("31 days before EOL a line is still required", Verdict(lines, new() { 10 }, new() { Job("a", 10) }, "2026-10-10").Problems.Count == 4);
    Ok("the 30-day boundary is leaving", IsLeaving("2026-12-07", "2026-11-07") && !IsLeaving("2026-12-08", "2026-11-07"));
    Ok("no EOL date is never leaving", !IsLeaving("none", "2099-01-01"));
    Ok("all leaving fails", Verdict(new() { new(9, "", "2026-11-10") }, new() { 9 }, new() { Job("a", 9) }, "2026-11-07").Problems.Count == 1);

    // Ahead of a GA: a conditional target element counts, and the target is a note, not a failure.
    Ok("conditional targets read too", string.Join(",", TestTargets(
        "<TargetFrameworks>net10.0</TargetFrameworks>\n<TargetFrameworks Condition=\"$(X) &gt;= 11\">net10.0;net11.0</TargetFrameworks>")) == "10,11");
    Ok("upcoming lines are go-live / preview", string.Join(",", UpcomingLines(index)) == "11");
    var ahead = Verdict(new() { new(10, "", "2028-11-14") }, new() { 10, 11 }, new() { Job("a", 10, 11) }, "2026-11-08", new() { 11 });
    Ok("a target ahead of its GA is a note, not a failure", ahead.Problems.Count == 0 && ahead.Notes.Count(n => n.Contains("ahead of its GA")) == 1);

    Console.WriteLine($"selftest: {passed}/{passed + failed} passed");
    return failed == 0 ? 0 : 1;
}

var cmd = Args.Count > 0 ? Args[0] : null;
try
{
    Environment.Exit(cmd == "selftest" ? Selftest() : cmd is null ? await Check() : Usage());
}
catch (Exception e)
{
    Console.Error.WriteLine($"runtimes: {e.Message}");
    Environment.Exit(1);
}

static int Usage()
{
    Console.Error.WriteLine("usage: runtimes.csx [selftest]");
    return 2;
}
