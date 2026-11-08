// The fixtures in `tests/` are exactly the stamped reference set from universalWasmLoader-js
// (workspace letter H7 / S4; SPEC §7: "without modification"). Same rule as -js's
// `scripts/check-fixtures.ts`: fails when a stamped file is missing or its bytes differ from
// `tests/fixtures.json`, or when `tests/` holds a `.wasm` / `.wit` / `.wat` the stamp does not list.
//
//   dotnet10 tool run dotnet-script scripts/fixtures.csx [-- dir]   dir defaults to tests/ (another dir is for proving the check)
//
// C# script run by dotnet-script, pinned in dotnet-tools.json (owner, 2026-11-08: "move them");
// ported from fixtures.ts, whose output it reproduces line for line (the workspace port rule).
// Run it through the LOCAL tool (`dotnet10 tool run dotnet-script`): from a directory on D:, the
// global form `dotnet10 script` starts the bare dotnet-script shim, whose restore then finds the
// SDK-less system `dotnet` first on PATH and fails (measured 2026-11-08).

#nullable enable
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

static string ScriptDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

(JsonElement Stamp, List<string> Problems) FixtureProblems(string dir)
{
    var stamp = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "fixtures.json"))).RootElement;
    var files = stamp.GetProperty("files");
    var problems = new List<string>();
    foreach (var entry in files.EnumerateObject())
    {
        var p = Path.Combine(dir, entry.Name);
        if (!File.Exists(p)) problems.Add($"{entry.Name}: missing");
        else if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))) != entry.Value.GetString())
            problems.Add($"{entry.Name}: bytes differ from the stamp");
    }
    foreach (var full in Directory.EnumerateFileSystemEntries(dir))
    {
        var f = Path.GetFileName(full);
        if (Regex.IsMatch(f, @"\.(wasm|wit|wat)$") && !files.TryGetProperty(f, out _)) problems.Add($"{f}: not in the stamp");
    }
    return (stamp, problems);
}

var dir = Args.Count > 0 ? Args[0] : Path.GetFullPath(Path.Combine(ScriptDir(), "..", "tests"));
var (stamp, problems) = FixtureProblems(dir);
if (problems.Count > 0)
{
    Console.WriteLine(string.Join("\n", new[] { "Fixtures do not match tests/fixtures.json:" }.Concat(problems.Select(p => $"  {p}"))));
    Console.WriteLine("Re-copy tests/*.wasm, *.wit, *.wat and fixtures.json from universalWasmLoader-js; never edit a fixture by hand.");
    Environment.Exit(1);
}
var generator = stamp.GetProperty("generator");
Console.WriteLine(
    $"{stamp.GetProperty("files").EnumerateObject().Count()} fixtures match the stamp (wasmtk {generator.GetProperty("describe").GetString()}, " +
    $"{generator.GetProperty("commit").GetString()![..11]})");
