using System.Text.RegularExpressions;

namespace UniversalWasmLoader;

/// <summary>One of the loader's supported WIT primitive types.</summary>
internal enum WitType { S32, S64, F32, F64, Bool, String }

/// <summary>A single WIT function parameter: its camelCase name and type.</summary>
internal readonly record struct WitParam(string Name, WitType Type);

/// <summary>
/// One WIT import or export. A WIT name has three forms: kebab-case (source identity),
/// camelCase (the actual WASM binary export/import symbol — used for lookups), and a
/// lang-idiomatic API name. The camelCase form is authoritative for runtime lookups.
/// </summary>
internal sealed record WitFunc(string Name, string TsName, IReadOnlyList<WitParam> Params, WitType? Result);

/// <summary>Result of parsing a <c>.wit</c> source produced by wasmtk.</summary>
internal sealed record ParsedWit(string PackageName, string WorldName, IReadOnlyList<WitFunc> Imports, IReadOnlyList<WitFunc> Exports);

/// <summary>
/// Parses the <c>.wit</c> subset wasmtk writes (SPEC 4.0.0 §2 "Parsing"): an optional
/// <c>package</c> line and one <c>world</c> holding only <c>import</c> / <c>export</c> function
/// statements, with <c>//</c> comments. Anything else throws a <see cref="FormatException"/> naming
/// the fault; a <c>.wit</c> is never read loosely. A port of the JS reference <c>wit-parser.js</c>
/// (loader-js 2.0.0): same grammar, same messages.
/// </summary>
internal static partial class Wit
{
    /// <summary>The WIT types this loader translates (SPEC §4); any other type is an error.</summary>
    private static readonly string[] SupportedTypes = { "s32", "s64", "f32", "f64", "bool", "string" };

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9]+)*\z")]
    private static partial Regex Ident();

    [GeneratedRegex(@"-([a-z0-9])")]
    private static partial Regex KebabHump();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\Apackage\s+([^\s;]+)\s*;")]
    private static partial Regex PackageRe();

    [GeneratedRegex(@"\Aworld\s+([^\s{]+)\s*\{([^{}]*)\}\z")]
    private static partial Regex WorldRe();

    [GeneratedRegex(@"\A(import|export)\s+([^\s:]+)\s*:\s*func\s*\(([^()]*)\)\s*(?:->\s*(\S+))?\z")]
    private static partial Regex FuncStmt();

    [GeneratedRegex(@"\A([^:\s]+)\s*:\s*(\S+)\z")]
    private static partial Regex ParamRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Convert a kebab-case WIT name to camelCase (e.g. <c>is-positive</c> → <c>isPositive</c>).</summary>
    public static string KebabToCamel(string name) =>
        KebabHump().Replace(name, m => m.Groups[1].Value.ToUpperInvariant());

    /// <summary>Convert a kebab-case WIT import name to the underscore WASM import key (e.g. <c>env-mul</c> → <c>env_mul</c>).</summary>
    public static string KebabToWasmImportKey(string name) => name.Replace('-', '_');

    /// <summary>The WIT spelling of a type (<c>s32</c>, <c>string</c>, …).</summary>
    public static string Name(WitType t) => t switch
    {
        WitType.S32 => "s32",
        WitType.S64 => "s64",
        WitType.F32 => "f32",
        WitType.F64 => "f64",
        WitType.Bool => "bool",
        _ => "string",
    };

    private static WitType ParseType(string raw, string where) => raw.Trim() switch
    {
        "s32" => WitType.S32,
        "s64" => WitType.S64,
        "f32" => WitType.F32,
        "f64" => WitType.F64,
        "bool" => WitType.Bool,
        "string" => WitType.String,
        var t => throw new FormatException(
            $"unsupported WIT type \"{t}\" in {where} (supported: {string.Join(", ", SupportedTypes)})"),
    };

    private static List<WitParam> ParseParams(string raw, string fnLabel)
    {
        // A trailing comma is valid WIT: `func(a: s32,)`.
        var parts = raw.Split(',').Select(p => p.Trim()).ToList();
        if (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        var result = new List<WitParam>(parts.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var m = ParamRe().Match(part);
            if (!m.Success || !Ident().IsMatch(m.Groups[1].Value))
                throw new FormatException($"malformed parameter \"{part}\" in {fnLabel} (expected \"name: type\")");
            var rawName = m.Groups[1].Value;
            if (!seen.Add(rawName)) throw new FormatException($"duplicate parameter \"{rawName}\" in {fnLabel}");
            var type = ParseType(m.Groups[2].Value, $"{fnLabel}, parameter {rawName}");
            result.Add(new WitParam(KebabToCamel(rawName), type));
        }
        return result;
    }

    /// <summary>Parse a <c>.wit</c> source. Throws <see cref="FormatException"/> on anything outside the grammar.</summary>
    public static ParsedWit Parse(string src)
    {
        var code = LineComment().Replace(src.TrimStart('﻿'), "");

        var rest = code.Trim();
        var packageName = "";
        var pkg = PackageRe().Match(rest);
        if (pkg.Success)
        {
            packageName = pkg.Groups[1].Value;
            rest = rest[pkg.Length..].Trim();
        }

        var world = WorldRe().Match(rest);
        if (!world.Success)
        {
            var what = rest.Length == 0
                ? "it is empty"
                : $"expected one \"world <name> {{ ... }}\" block, found: {rest[..Math.Min(60, rest.Length)]}";
            throw new FormatException($"not a WIT world: {what}");
        }
        var worldName = world.Groups[1].Value;
        if (!Ident().IsMatch(worldName)) throw new FormatException($"malformed world name \"{worldName}\"");

        var imports = new List<WitFunc>();
        var exports = new List<WitFunc>();
        var statements = world.Groups[2].Value.Split(';').Select(s => s.Trim()).ToList();
        var last = statements[^1];
        statements.RemoveAt(statements.Count - 1);
        if (last.Length != 0) throw new FormatException($"unterminated WIT statement (missing \";\"): {last}");
        foreach (var stmt in statements)
        {
            var m = FuncStmt().Match(stmt);
            if (!m.Success)
                throw new FormatException($"unsupported or malformed WIT statement: {Spaces().Replace(stmt, " ")}");
            var keyword = m.Groups[1].Value;
            var name = m.Groups[2].Value;
            var fnLabel = $"{keyword} {name}";
            if (!Ident().IsMatch(name)) throw new FormatException($"malformed function name in {fnLabel}");
            var list = keyword == "import" ? imports : exports;
            if (list.Any(f => f.Name == name)) throw new FormatException($"duplicate {fnLabel}");
            var ps = ParseParams(m.Groups[3].Value, fnLabel);
            WitType? result = m.Groups[4].Success ? ParseType(m.Groups[4].Value, $"{fnLabel}, result") : null;
            list.Add(new WitFunc(name, KebabToCamel(name), ps, result));
        }

        return new ParsedWit(packageName, worldName, imports, exports);
    }
}
