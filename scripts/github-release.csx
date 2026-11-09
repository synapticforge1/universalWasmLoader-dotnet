// Create the GitHub Release page for a tag, through GitHub's REST API with the job's GITHUB_TOKEN:
// never the GitHub CLI (workspace letter H30), never by hand (letter H32, owner 2026-11-09: every
// project's release CI makes its page; only NEW tags get one).
//
// H32's order, kept exactly:
//   (1) the tag exists on GitHub and peels (annotated tags followed) to the released commit,
//       because POST /releases would CREATE a missing tag; otherwise refuse, with no write;
//   (2) a Release already there = done, with no write (re-runs are safe);
//   (3) POST with `generate_release_notes` and the nuget.org link on top, attach the packages,
//       then read the page back (this tag, not a draft) and peel the tag again.
// Every request has its own 30 s limit, request to last byte (workspace best-practices 7.21).
//
//   dotnet10 tool run dotnet-script scripts/github-release.csx -- <tag> <commit-sha> <dir> [--dry-run]
//   dotnet10 tool run dotnet-script scripts/github-release.csx -- selftest
//
// Environment: GITHUB_REPOSITORY (owner/name), GITHUB_TOKEN (not needed for --dry-run, which makes
// only GET requests and prints what it would do), GITHUB_API_URL (default https://api.github.com).
// The assets are every *.nupkg and *.snupkg directly in <dir>. Only the .NET base library is used.
// `selftest` runs the whole flow against a fake GitHub API (no network): the mock-API tests H32 asks for.
// C# script run by dotnet-script, pinned in dotnet-tools.json (working-rules.md).

#nullable enable
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

const int CALL_SECONDS = 30;
const string NUGET_PAGE = "https://www.nuget.org/packages/UniversalWasmLoader";

/// The tag must be a plain release tag: it goes into URLs and the Release title.
static bool ValidTag(string tag) => Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+$");

static bool ValidSha(string sha) => Regex.IsMatch(sha, @"^[0-9a-f]{40}$");

static bool ValidRepository(string repo) => Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");

/// The files to attach, by name, sorted: every *.nupkg and *.snupkg directly in `dir`.
static List<string> AssetFiles(IEnumerable<string> fileNames) =>
    fileNames.Where(n => n.EndsWith(".nupkg", StringComparison.Ordinal) || n.EndsWith(".snupkg", StringComparison.Ordinal))
        .OrderBy(n => n, StringComparer.Ordinal).ToList();

/// The JSON body that creates the Release: the nuget.org link on top of GitHub's generated notes.
static string CreateBody(string tag) =>
    JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["tag_name"] = tag,
        ["name"] = tag,
        ["body"] = $"NuGet: {NUGET_PAGE}/{tag[1..]}",
        ["generate_release_notes"] = true,
    });

/// The upload URL for one asset, from the Release's `upload_url` template (`…/assets{?name,label}`).
static string UploadUrl(string template, string name) =>
    Regex.Replace(template, @"\{\?[^}]*\}$", "") + "?name=" + Uri.EscapeDataString(name);

class Refused : Exception
{
    public Refused(string message) : base(message) { }
}

/// One GitHub API session: every call has its own time limit; `log` receives every line printed.
class Api
{
    private readonly HttpClient _http;
    private readonly string _api, _repo;
    private readonly int _seconds;
    public readonly List<string> Writes = new();

    public Api(HttpMessageHandler handler, string api, string repo, string? token, int seconds)
    {
        _http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("universalWasmLoader-dotnet-release");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrEmpty(token)) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _api = api.TrimEnd('/');
        _repo = repo;
        _seconds = seconds;
    }

    public string Repo(string path) => $"{_api}/repos/{_repo}{path}";

    /// One request with its own limit, request to last byte. Returns status and body.
    public async Task<(HttpStatusCode Status, string Body)> Send(HttpMethod method, string url, HttpContent? content = null)
    {
        if (method != HttpMethod.Get) Writes.Add($"{method} {url}");
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(_seconds));
        try
        {
            using var req = new HttpRequestMessage(method, url) { Content = content };
            using var res = await _http.SendAsync(req, limit.Token);
            return (res.StatusCode, await res.Content.ReadAsStringAsync(limit.Token));
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            throw new Exception($"no complete response from {url} within {_seconds} s");
        }
    }

    public static string Must((HttpStatusCode Status, string Body) r, string what) =>
        (int)r.Status is >= 200 and < 300 ? r.Body
            : throw new Exception($"{what}: HTTP {(int)r.Status}: {r.Body[..Math.Min(300, r.Body.Length)]}");

    /// The commit a tag points at, annotated tags followed; null when the tag does not exist.
    public async Task<string?> PeelTag(string tag)
    {
        var r = await Send(HttpMethod.Get, Repo($"/git/ref/tags/{tag}"));
        if (r.Status == HttpStatusCode.NotFound) return null;
        var obj = JsonDocument.Parse(Must(r, $"read tag {tag}")).RootElement.GetProperty("object");
        for (var hops = 0; obj.GetProperty("type").GetString() == "tag"; hops++)
        {
            if (hops > 5) throw new Exception($"tag {tag}: too many nested tag objects");
            var t = await Send(HttpMethod.Get, Repo($"/git/tags/{obj.GetProperty("sha").GetString()}"));
            obj = JsonDocument.Parse(Must(t, $"read tag object of {tag}")).RootElement.GetProperty("object");
        }
        return obj.GetProperty("type").GetString() == "commit" ? obj.GetProperty("sha").GetString() : null;
    }
}

static async Task Release(Api api, string tag, string sha, string dir, bool dryRun, Action<string> log)
{
    var wanted = AssetFiles(Directory.GetFiles(dir).Select(Path.GetFileName).Select(n => n!));
    if (wanted.Count == 0) throw new Refused($"no .nupkg / .snupkg in '{dir}'");

    // (1) The tag exists and peels to the released commit, or nothing is written.
    var at = await api.PeelTag(tag);
    if (at is null) throw new Refused($"tag {tag} does not exist on GitHub: refusing (POST /releases would create it)");
    if (at != sha) throw new Refused($"tag {tag} points at {at}, not the released commit {sha}: refusing");
    log($"tag {tag} peels to {sha}");

    // (2) A Release already there = done, with no write.
    var existing = await api.Send(HttpMethod.Get, api.Repo($"/releases/tags/{tag}"));
    if (existing.Status != HttpStatusCode.NotFound)
    {
        var page = JsonDocument.Parse(Api.Must(existing, "read the Release")).RootElement;
        log($"the Release {tag} already exists: {page.GetProperty("html_url").GetString()}; nothing to do");
        return;
    }

    // (3) Create it, attach the packages, read it back, peel the tag again.
    if (dryRun)
    {
        log($"[dry run] would create the Release {tag} (nuget.org link on top of the generated notes)");
        foreach (var w in wanted) log($"[dry run] would upload {w}");
        return;
    }
    var created = JsonDocument.Parse(Api.Must(
        await api.Send(HttpMethod.Post, api.Repo("/releases"), new StringContent(CreateBody(tag), Encoding.UTF8, "application/json")),
        "create the Release")).RootElement;
    log($"created the Release {tag}");
    foreach (var name in wanted)
    {
        var bytes = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(dir, name)));
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        Api.Must(await api.Send(HttpMethod.Post, UploadUrl(created.GetProperty("upload_url").GetString()!, name), bytes), $"upload {name}");
        log($"uploaded {name}");
    }
    var back = JsonDocument.Parse(Api.Must(await api.Send(HttpMethod.Get, api.Repo($"/releases/tags/{tag}")), "read the Release back")).RootElement;
    if (back.GetProperty("tag_name").GetString() != tag) throw new Exception($"read back: the Release names tag {back.GetProperty("tag_name").GetString()}, not {tag}");
    if (back.GetProperty("draft").GetBoolean()) throw new Exception($"read back: the Release {tag} is a draft");
    var attached = back.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("name").GetString()!).ToList();
    var absent = wanted.Where(w => !attached.Contains(w)).ToList();
    if (absent.Count > 0) throw new Exception($"read back: assets missing: {string.Join(", ", absent)}");
    var again = await api.PeelTag(tag);
    if (again != sha) throw new Exception($"tag {tag} moved during the release: now {again ?? "(gone)"}, released {sha}");
    log($"read back: {back.GetProperty("html_url").GetString()} (tag {tag}, not a draft, {attached.Count} assets; tag still at {sha})");
}

// ---------------------------------------------------------------- the fake GitHub API (selftest)

/// A fake GitHub REST API: tags, tag objects and Releases in memory; records every write.
class FakeGitHub : HttpMessageHandler
{
    public Dictionary<string, (string Type, string Sha)> Refs = new();      // tag -> object
    public Dictionary<string, (string Type, string Sha)> TagObjects = new(); // annotated tag sha -> object
    public Dictionary<string, (bool Draft, List<string> Assets)> Releases = new();
    public HttpStatusCode? FailCreate;
    public bool CreateAsDraft, StallEverything;
    public Action? AfterCreate;
    public int Posts;

    static HttpResponseMessage Json(HttpStatusCode code, object body) =>
        new(code) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    object Page(string tag) => new Dictionary<string, object>
    {
        ["tag_name"] = tag,
        ["draft"] = Releases[tag].Draft,
        ["html_url"] = $"https://github.com/o/r/releases/tag/{tag}",
        ["upload_url"] = $"https://uploads.example/releases/{tag}/assets{{?name,label}}",
        ["assets"] = Releases[tag].Assets.Select(a => new Dictionary<string, object> { ["name"] = a }).ToList(),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (StallEverything) await Task.Delay(System.Threading.Timeout.Infinite, ct);
        var path = req.RequestUri!.AbsolutePath;
        Match m;
        if (req.Method == HttpMethod.Get && (m = Regex.Match(path, "/git/ref/tags/(.+)$")).Success)
            return Refs.TryGetValue(m.Groups[1].Value, out var o)
                ? Json(HttpStatusCode.OK, new { @object = new { type = o.Type, sha = o.Sha } })
                : Json(HttpStatusCode.NotFound, new { message = "Not Found" });
        if (req.Method == HttpMethod.Get && (m = Regex.Match(path, "/git/tags/(.+)$")).Success)
            return Json(HttpStatusCode.OK, new { @object = new { type = TagObjects[m.Groups[1].Value].Type, sha = TagObjects[m.Groups[1].Value].Sha } });
        if (req.Method == HttpMethod.Get && (m = Regex.Match(path, "/releases/tags/(.+)$")).Success)
            return Releases.ContainsKey(m.Groups[1].Value) ? Json(HttpStatusCode.OK, Page(m.Groups[1].Value)) : Json(HttpStatusCode.NotFound, new { message = "Not Found" });
        if (req.Method == HttpMethod.Post && path.EndsWith("/releases"))
        {
            Posts++;
            if (FailCreate is HttpStatusCode code) return Json(code, new { message = "Validation Failed" });
            var tag = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct)).RootElement.GetProperty("tag_name").GetString()!;
            Releases[tag] = (CreateAsDraft, new List<string>());
            AfterCreate?.Invoke();
            return Json(HttpStatusCode.Created, Page(tag));
        }
        if (req.Method == HttpMethod.Post && (m = Regex.Match(path, "/releases/(.+)/assets$")).Success)
        {
            Releases[m.Groups[1].Value].Assets.Add(Uri.UnescapeDataString(req.RequestUri.Query.Replace("?name=", "")));
            return Json(HttpStatusCode.Created, new { });
        }
        return Json(HttpStatusCode.NotFound, new { message = $"fake: no route {req.Method} {path}" });
    }
}

static async Task<int> Selftest()
{
    int passed = 0, failed = 0;
    void Ok(string name, bool cond)
    {
        if (cond) passed++;
        else { failed++; Console.Error.WriteLine($"selftest FAILED: {name}"); }
    }
    const string SHA = "1111111111111111111111111111111111111111", OTHER = "2222222222222222222222222222222222222222", TAGOBJ = "3333333333333333333333333333333333333333";
    var dir = Directory.CreateTempSubdirectory("uwl-release-test-").FullName;
    try
    {
        foreach (var n in new[] { "P.0.3.0.nupkg", "P.0.3.0.snupkg", "notes.txt" }) File.WriteAllText(Path.Combine(dir, n), "x");
        async Task<(FakeGitHub Fake, Exception? Error, List<string> Log, Api Api)> Run(Action<FakeGitHub> setup, bool dryRun = false, int seconds = CALL_SECONDS)
        {
            var fake = new FakeGitHub();
            setup(fake);
            var api = new Api(fake, "https://api.example", "o/r", "t", seconds);
            var log = new List<string>();
            try { await Release(api, "v0.3.0", SHA, dir, dryRun, log.Add); return (fake, null, log, api); }
            catch (Exception e) { return (fake, e, log, api); }
        }

        var r = await Run(f => f.Refs["v0.3.0"] = ("commit", SHA));
        Ok("lightweight tag: created, both assets, read back", r.Error is null && r.Fake.Releases["v0.3.0"].Assets.Count == 2 && r.Log.Last().StartsWith("read back:"));
        Ok("create body: nuget link on top + generated notes", CreateBody("v0.3.0").Contains($"{NUGET_PAGE}/0.3.0") && CreateBody("v0.3.0").Contains("\"generate_release_notes\":true"));

        r = await Run(f => { f.Refs["v0.3.0"] = ("tag", TAGOBJ); f.TagObjects[TAGOBJ] = ("commit", SHA); });
        Ok("annotated tag: peeled and created", r.Error is null && r.Fake.Posts == 1);

        r = await Run(f => { });
        Ok("missing tag: refused, no write", r.Error is Refused && r.Error.Message.Contains("does not exist") && r.Api.Writes.Count == 0);

        r = await Run(f => f.Refs["v0.3.0"] = ("commit", OTHER));
        Ok("tag at another commit: refused, no write", r.Error is Refused && r.Error.Message.Contains(OTHER) && r.Api.Writes.Count == 0);

        r = await Run(f => { f.Refs["v0.3.0"] = ("tag", TAGOBJ); f.TagObjects[TAGOBJ] = ("commit", OTHER); });
        Ok("annotated tag at another commit: refused, no write", r.Error is Refused && r.Api.Writes.Count == 0);

        r = await Run(f => { f.Refs["v0.3.0"] = ("commit", SHA); f.Releases["v0.3.0"] = (false, new List<string>()); });
        Ok("existing page: done, no write", r.Error is null && r.Api.Writes.Count == 0 && r.Log.Last().Contains("nothing to do"));

        r = await Run(f => f.Refs["v0.3.0"] = ("commit", SHA), dryRun: true);
        Ok("dry run: no write", r.Error is null && r.Api.Writes.Count == 0 && r.Log.Any(l => l.Contains("would create")));

        r = await Run(f => { f.Refs["v0.3.0"] = ("commit", SHA); f.FailCreate = HttpStatusCode.UnprocessableEntity; });
        Ok("POST 422: error naming the status", r.Error is not null && r.Error.Message.Contains("HTTP 422"));

        r = await Run(f => { f.Refs["v0.3.0"] = ("commit", SHA); f.CreateAsDraft = true; });
        Ok("read back a draft: error", r.Error is not null && r.Error.Message.Contains("draft"));

        r = await Run(f => { f.Refs["v0.3.0"] = ("commit", SHA); f.AfterCreate = () => f.Refs["v0.3.0"] = ("commit", OTHER); });
        Ok("tag moved after the POST: error", r.Error is not null && r.Error.Message.Contains("moved"));

        // Guarded: a missing time limit must FAIL this case, not hang the self-test.
        var stalled = Run(f => f.StallEverything = true, seconds: 1);
        var stallDone = await Task.WhenAny(stalled, Task.Delay(TimeSpan.FromSeconds(10))) == stalled;
        Ok("stalled API: fails within the limit, naming it",
            stallDone && stalled.Result.Error is not null && stalled.Result.Error.Message.Contains("within 1 s"));

        Ok("tag / sha / repo / asset validation",
            ValidTag("v0.2.1") && !ValidTag("v0.2.1-rc.1") && !ValidTag("main") && ValidSha(SHA) && !ValidSha("abc")
            && ValidRepository("o/r") && !ValidRepository("a/b/c")
            && string.Join("|", AssetFiles(new[] { "x.snupkg", "README.md", "x.nupkg" })) == "x.nupkg|x.snupkg"
            && UploadUrl("https://u/r/1/assets{?name,label}", "A b.nupkg") == "https://u/r/1/assets?name=A%20b.nupkg");
    }
    finally
    {
        Directory.Delete(dir, recursive: true);
    }
    Console.WriteLine($"selftest: {passed}/{passed + failed} passed");
    return failed == 0 ? 0 : 1;
}

// ---------------------------------------------------------------- main

try
{
    if (Args.Count == 1 && Args[0] == "selftest") Environment.Exit(await Selftest());
    if (Args.Count is 3 or 4 && (Args.Count == 3 || Args[3] == "--dry-run"))
    {
        var (tag, sha, dir, dryRun) = (Args[0], Args[1], Args[2], Args.Count == 4);
        if (!ValidTag(tag)) throw new Refused($"not a release tag: '{tag}' (expected vX.Y.Z)");
        if (!ValidSha(sha)) throw new Refused($"not a full commit sha: '{sha}'");
        var repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "";
        if (!ValidRepository(repo)) throw new Refused($"GITHUB_REPOSITORY must be owner/name, got '{repo}'");
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!dryRun && string.IsNullOrEmpty(token)) throw new Refused("GITHUB_TOKEN is not set");
        if (!Directory.Exists(dir)) throw new Refused($"no directory '{dir}'");
        var api = new Api(new HttpClientHandler(), Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com",
            repo, dryRun ? null : token, CALL_SECONDS);
        await Release(api, tag, sha, dir, dryRun, Console.WriteLine);
        Environment.Exit(0);
    }
    Console.Error.WriteLine("usage: github-release.csx <tag> <commit-sha> <dir> [--dry-run] | selftest");
    Environment.Exit(2);
}
catch (Exception e)
{
    Console.Error.WriteLine($"github-release: {e.Message}");
    Environment.Exit(1);
}
