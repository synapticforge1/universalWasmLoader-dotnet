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
/// Parses the <c>.wit</c> source produced by wasmtk. Mirrors the JS reference
/// <c>wit-parser.js</c> exactly (regex shapes, kebab→camel conversion, s32 fallback).
/// </summary>
internal static partial class Wit
{
    /// <summary>Convert a kebab-case WIT name to camelCase (e.g. <c>is-positive</c> → <c>isPositive</c>).</summary>
    public static string KebabToCamel(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        bool upper = false;
        foreach (char c in name)
        {
            if (c == '-') { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return sb.ToString();
    }

    /// <summary>Convert a kebab-case WIT import name to the underscore WASM import key (e.g. <c>env-mul</c> → <c>env_mul</c>).</summary>
    public static string KebabToWasmImportKey(string name) => name.Replace('-', '_');

    private static WitType ParseType(string raw) => raw.Trim() switch
    {
        "s32" => WitType.S32,
        "s64" => WitType.S64,
        "f32" => WitType.F32,
        "f64" => WitType.F64,
        "bool" => WitType.Bool,
        "string" => WitType.String,
        _ => WitType.S32, // unknown/aggregate falls back to s32 (a pointer still passes through)
    };

    private static List<WitParam> ParseParams(string raw)
    {
        var result = new List<WitParam>();
        raw = raw.Trim();
        if (raw.Length == 0) return result;
        foreach (var part in raw.Split(','))
        {
            int colon = part.IndexOf(':');
            if (colon < 0) continue;
            var name = part[..colon].Trim();
            result.Add(new WitParam(KebabToCamel(name), ParseType(part[(colon + 1)..])));
        }
        return result;
    }

    private static List<WitFunc> ParseFuncs(string body, string keyword)
    {
        var re = new Regex(
            $@"\b{keyword}\s+([\w-]+)\s*:\s*func\s*\(([^)]*)\)(?:\s*->\s*([\w-]+))?\s*;",
            RegexOptions.Compiled);
        var funcs = new List<WitFunc>();
        foreach (Match m in re.Matches(body))
        {
            var name = m.Groups[1].Value;
            WitType? result = m.Groups[3].Success && m.Groups[3].Value.Length > 0
                ? ParseType(m.Groups[3].Value)
                : null;
            funcs.Add(new WitFunc(name, KebabToCamel(name), ParseParams(m.Groups[2].Value), result));
        }
        return funcs;
    }

    [GeneratedRegex(@"package\s+([\w:/-]+)\s*;")]
    private static partial Regex PackageRe();

    [GeneratedRegex(@"world\s+([\w-]+)\s*\{([\s\S]*)\}")]
    private static partial Regex WorldRe();

    public static ParsedWit Parse(string src)
    {
        var pkg = PackageRe().Match(src);
        var world = WorldRe().Match(src);
        var body = world.Success ? world.Groups[2].Value : "";
        return new ParsedWit(
            pkg.Success ? pkg.Groups[1].Value : "",
            world.Success ? world.Groups[1].Value : "",
            ParseFuncs(body, "import"),
            ParseFuncs(body, "export"));
    }
}
