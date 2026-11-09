// Create the GitHub Release page for a tag, through GitHub's REST API with the job's GITHUB_TOKEN:
// never the GitHub CLI (workspace letter H30), never by hand (letter H32, owner 2026-11-09: every
// project's release CI makes its page; only NEW tags get one).
//
// Bun TypeScript like the repo's other scripts (owner, 2026-11-09: "use bun instead
// of dotnet script to release"): the `release` job holds `contents: write`, and with Bun installed
// by hand (pinned by sha256) and no package dependencies, no third-party package code runs beside
// that token (H32's recipe, C30). Ported from `github-release.csx`, whose output it reproduces line
// for line (the workspace port rule; the proof is in cmem/overview.md).
//
// H32's order, kept exactly:
//   (1) the tag exists on GitHub and peels (annotated tags followed) to the released commit,
//       because POST /releases would CREATE a missing tag; otherwise refuse, with no write;
//   (2) a Release already there = done, with no write (re-runs are safe);
//   (3) POST with `generate_release_notes` and the nuget.org link on top, attach the packages,
//       then read the page back (this tag, not a draft) and peel the tag again.
// Every request has its own 30 s limit, request to last byte (workspace best-practices 7.21).
//
//   bun scripts/github-release.ts <tag> <commit-sha> <dir> [--dry-run]
//   bun scripts/github-release.ts selftest
//
// Environment: GITHUB_REPOSITORY (owner/name), GITHUB_TOKEN (not needed for --dry-run, which makes
// only GET requests and prints what it would do), GITHUB_API_URL (default https://api.github.com).
// The assets are every *.nupkg and *.snupkg directly in <dir>. No imports beyond `node:*`.
// `selftest` runs the whole flow against a fake GitHub API (no network): H32's mock-API tests.

import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import process from "node:process";

const CALL_SECONDS = 30;
const NUGET_PAGE = "https://www.nuget.org/packages/UniversalWasmLoader";

/** The tag must be a plain release tag: it goes into URLs and the Release title. */
export const validTag = (tag: string) => /^v[0-9]+\.[0-9]+\.[0-9]+$/.test(tag);
export const validSha = (sha: string) => /^[0-9a-f]{40}$/.test(sha);
export const validRepository = (repo: string) => /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repo);

/** The files to attach, by name, sorted: every *.nupkg and *.snupkg directly in `dir`. */
export const assetFiles = (fileNames: string[]) =>
  fileNames.filter((n) => n.endsWith(".nupkg") || n.endsWith(".snupkg")).sort();

/** The JSON body that creates the Release: the nuget.org link on top of GitHub's generated notes. */
export const createBody = (tag: string) =>
  JSON.stringify({ tag_name: tag, name: tag, body: `NuGet: ${NUGET_PAGE}/${tag.slice(1)}`, generate_release_notes: true });

/** The upload URL for one asset, from the Release's `upload_url` template (`…/assets{?name,label}`). */
export const uploadUrl = (template: string, name: string) =>
  template.replace(/\{\?[^}]*\}$/, "") + "?name=" + encodeURIComponent(name);

export class Refused extends Error {}

type Fetch = (url: string, init: RequestInit) => Promise<Response>;
type Reply = { status: number; body: string };

/** One GitHub API session: every call has its own time limit; writes are recorded. */
export class Api {
  readonly writes: string[] = [];
  constructor(
    private readonly fetchImpl: Fetch,
    private readonly api: string,
    private readonly repoName: string,
    private readonly token: string | undefined,
    private readonly seconds: number,
  ) {
    this.api = api.replace(/\/+$/, "");
  }

  repo(path: string) {
    return `${this.api}/repos/${this.repoName}${path}`;
  }

  /** One request with its own limit, request to last byte. */
  async send(method: string, url: string, body?: BodyInit, contentType?: string): Promise<Reply> {
    if (method !== "GET") this.writes.push(`${method} ${url}`);
    const limit = new AbortController();
    const timer = setTimeout(() => limit.abort(), this.seconds * 1000);
    try {
      const headers: Record<string, string> = {
        "User-Agent": "universalWasmLoader-dotnet-release",
        Accept: "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
      };
      if (this.token) headers.Authorization = `Bearer ${this.token}`;
      if (contentType) headers["Content-Type"] = contentType;
      const res = await this.fetchImpl(url, { method, headers, body, signal: limit.signal });
      return { status: res.status, body: await res.text() };
    } catch (e) {
      if (limit.signal.aborted) throw new Error(`no complete response from ${url} within ${this.seconds} s`);
      throw e;
    } finally {
      clearTimeout(timer);
    }
  }

  static must(r: Reply, what: string): string {
    if (r.status >= 200 && r.status < 300) return r.body;
    throw new Error(`${what}: HTTP ${r.status}: ${r.body.slice(0, 300)}`);
  }

  /** The commit a tag points at, annotated tags followed; null when the tag does not exist. */
  async peelTag(tag: string): Promise<string | null> {
    const r = await this.send("GET", this.repo(`/git/ref/tags/${tag}`));
    if (r.status === 404) return null;
    let obj = JSON.parse(Api.must(r, `read tag ${tag}`)).object;
    for (let hops = 0; obj.type === "tag"; hops++) {
      if (hops > 5) throw new Error(`tag ${tag}: too many nested tag objects`);
      obj = JSON.parse(Api.must(await this.send("GET", this.repo(`/git/tags/${obj.sha}`)), `read tag object of ${tag}`)).object;
    }
    return obj.type === "commit" ? obj.sha : null;
  }
}

export async function release(api: Api, tag: string, sha: string, dir: string, dryRun: boolean, log: (line: string) => void) {
  const wanted = assetFiles(readdirSync(dir).filter((n) => statSync(join(dir, n)).isFile()));
  if (wanted.length === 0) throw new Refused(`no .nupkg / .snupkg in '${dir}'`);

  // (1) The tag exists and peels to the released commit, or nothing is written.
  const at = await api.peelTag(tag);
  if (at === null) throw new Refused(`tag ${tag} does not exist on GitHub: refusing (POST /releases would create it)`);
  if (at !== sha) throw new Refused(`tag ${tag} points at ${at}, not the released commit ${sha}: refusing`);
  log(`tag ${tag} peels to ${sha}`);

  // (2) A Release already there = done, with no write.
  const existing = await api.send("GET", api.repo(`/releases/tags/${tag}`));
  if (existing.status !== 404) {
    const page = JSON.parse(Api.must(existing, "read the Release"));
    log(`the Release ${tag} already exists: ${page.html_url}; nothing to do`);
    return;
  }

  // (3) Create it, attach the packages, read it back, peel the tag again.
  if (dryRun) {
    log(`[dry run] would create the Release ${tag} (nuget.org link on top of the generated notes)`);
    for (const w of wanted) log(`[dry run] would upload ${w}`);
    return;
  }
  const created = JSON.parse(Api.must(await api.send("POST", api.repo("/releases"), createBody(tag), "application/json"), "create the Release"));
  log(`created the Release ${tag}`);
  for (const name of wanted) {
    Api.must(await api.send("POST", uploadUrl(created.upload_url, name), readFileSync(join(dir, name)), "application/octet-stream"), `upload ${name}`);
    log(`uploaded ${name}`);
  }
  const back = JSON.parse(Api.must(await api.send("GET", api.repo(`/releases/tags/${tag}`)), "read the Release back"));
  if (back.tag_name !== tag) throw new Error(`read back: the Release names tag ${back.tag_name}, not ${tag}`);
  if (back.draft) throw new Error(`read back: the Release ${tag} is a draft`);
  const attached: string[] = back.assets.map((a: { name: string }) => a.name);
  const absent = wanted.filter((w) => !attached.includes(w));
  if (absent.length > 0) throw new Error(`read back: assets missing: ${absent.join(", ")}`);
  const again = await api.peelTag(tag);
  if (again !== sha) throw new Error(`tag ${tag} moved during the release: now ${again ?? "(gone)"}, released ${sha}`);
  log(`read back: ${back.html_url} (tag ${tag}, not a draft, ${attached.length} assets; tag still at ${sha})`);
}

// ---------------------------------------------------------------- the fake GitHub API (selftest)

/** A fake GitHub REST API: tags, tag objects and Releases in memory. */
class FakeGitHub {
  refs = new Map<string, { type: string; sha: string }>();
  tagObjects = new Map<string, { type: string; sha: string }>();
  releases = new Map<string, { draft: boolean; assets: string[] }>();
  failCreate: number | null = null;
  createAsDraft = false;
  stallEverything = false;
  afterCreate: (() => void) | null = null;
  posts = 0;

  private json(status: number, body: unknown) {
    return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  }

  private page(tag: string) {
    const r = this.releases.get(tag)!;
    return {
      tag_name: tag,
      draft: r.draft,
      html_url: `https://github.com/o/r/releases/tag/${tag}`,
      upload_url: `https://uploads.example/releases/${tag}/assets{?name,label}`,
      assets: r.assets.map((name) => ({ name })),
    };
  }

  fetch: Fetch = async (url, init) => {
    if (this.stallEverything) {
      await new Promise((_, reject) => init.signal?.addEventListener("abort", () => reject(new Error("aborted"))));
    }
    const u = new URL(url);
    const path = u.pathname;
    const method = init.method ?? "GET";
    let m: RegExpExecArray | null;
    if (method === "GET" && (m = /\/git\/ref\/tags\/(.+)$/.exec(path))) {
      const o = this.refs.get(m[1]!);
      return o ? this.json(200, { object: o }) : this.json(404, { message: "Not Found" });
    }
    if (method === "GET" && (m = /\/git\/tags\/(.+)$/.exec(path))) return this.json(200, { object: this.tagObjects.get(m[1]!) });
    if (method === "GET" && (m = /\/releases\/tags\/(.+)$/.exec(path))) {
      return this.releases.has(m[1]!) ? this.json(200, this.page(m[1]!)) : this.json(404, { message: "Not Found" });
    }
    if (method === "POST" && path.endsWith("/releases")) {
      this.posts++;
      if (this.failCreate !== null) return this.json(this.failCreate, { message: "Validation Failed" });
      const tag = JSON.parse(String(init.body)).tag_name as string;
      this.releases.set(tag, { draft: this.createAsDraft, assets: [] });
      this.afterCreate?.();
      return this.json(201, this.page(tag));
    }
    if (method === "POST" && (m = /\/releases\/(.+)\/assets$/.exec(path))) {
      this.releases.get(m[1]!)!.assets.push(decodeURIComponent(u.searchParams.get("name") ?? ""));
      return this.json(201, {});
    }
    return this.json(404, { message: `fake: no route ${method} ${path}` });
  };
}

async function selftest(): Promise<number> {
  let passed = 0;
  let failed = 0;
  const ok = (name: string, cond: boolean) => {
    if (cond) passed++;
    else {
      failed++;
      console.error(`selftest FAILED: ${name}`);
    }
  };
  const SHA = "1".repeat(40);
  const OTHER = "2".repeat(40);
  const TAGOBJ = "3".repeat(40);
  const dir = mkdtempSync(join(tmpdir(), "uwl-release-test-"));
  try {
    for (const n of ["P.0.3.0.nupkg", "P.0.3.0.snupkg", "notes.txt"]) writeFileSync(join(dir, n), "x");
    const run = async (setup: (f: FakeGitHub) => void, dryRun = false, seconds = CALL_SECONDS) => {
      const fake = new FakeGitHub();
      setup(fake);
      const api = new Api(fake.fetch, "https://api.example", "o/r", "t", seconds);
      const log: string[] = [];
      try {
        await release(api, "v0.3.0", SHA, dir, dryRun, (l) => log.push(l));
        return { fake, error: null as Error | null, log, api };
      } catch (e) {
        return { fake, error: e as Error, log, api };
      }
    };

    let r = await run((f) => f.refs.set("v0.3.0", { type: "commit", sha: SHA }));
    ok("lightweight tag: created, both assets, read back",
      r.error === null && r.fake.releases.get("v0.3.0")!.assets.length === 2 && r.log.at(-1)!.startsWith("read back:"));
    ok("create body: nuget link on top + generated notes",
      createBody("v0.3.0").includes(`${NUGET_PAGE}/0.3.0`) && createBody("v0.3.0").includes('"generate_release_notes":true'));

    r = await run((f) => {
      f.refs.set("v0.3.0", { type: "tag", sha: TAGOBJ });
      f.tagObjects.set(TAGOBJ, { type: "commit", sha: SHA });
    });
    ok("annotated tag: peeled and created", r.error === null && r.fake.posts === 1);

    r = await run(() => {});
    ok("missing tag: refused, no write", r.error instanceof Refused && r.error.message.includes("does not exist") && r.api.writes.length === 0);

    r = await run((f) => f.refs.set("v0.3.0", { type: "commit", sha: OTHER }));
    ok("tag at another commit: refused, no write", r.error instanceof Refused && r.error.message.includes(OTHER) && r.api.writes.length === 0);

    r = await run((f) => {
      f.refs.set("v0.3.0", { type: "tag", sha: TAGOBJ });
      f.tagObjects.set(TAGOBJ, { type: "commit", sha: OTHER });
    });
    ok("annotated tag at another commit: refused, no write", r.error instanceof Refused && r.api.writes.length === 0);

    r = await run((f) => {
      f.refs.set("v0.3.0", { type: "commit", sha: SHA });
      f.releases.set("v0.3.0", { draft: false, assets: [] });
    });
    ok("existing page: done, no write", r.error === null && r.api.writes.length === 0 && r.log.at(-1)!.includes("nothing to do"));

    r = await run((f) => f.refs.set("v0.3.0", { type: "commit", sha: SHA }), true);
    ok("dry run: no write", r.error === null && r.api.writes.length === 0 && r.log.some((l) => l.includes("would create")));

    r = await run((f) => {
      f.refs.set("v0.3.0", { type: "commit", sha: SHA });
      f.failCreate = 422;
    });
    ok("POST 422: error naming the status", r.error !== null && r.error.message.includes("HTTP 422"));

    r = await run((f) => {
      f.refs.set("v0.3.0", { type: "commit", sha: SHA });
      f.createAsDraft = true;
    });
    ok("read back a draft: error", r.error !== null && r.error.message.includes("draft"));

    r = await run((f) => {
      f.refs.set("v0.3.0", { type: "commit", sha: SHA });
      f.afterCreate = () => f.refs.set("v0.3.0", { type: "commit", sha: OTHER });
    });
    ok("tag moved after the POST: error", r.error !== null && r.error.message.includes("moved"));

    // Guarded: a missing time limit must FAIL this case, not hang the self-test.
    const stalled = run((f) => (f.stallEverything = true), false, 1);
    const guard = new Promise<null>((res) => setTimeout(() => res(null), 10_000));
    const stall = await Promise.race([stalled, guard]);
    ok("stalled API: fails within the limit, naming it", stall !== null && stall.error !== null && stall.error.message.includes("within 1 s"));

    ok("tag / sha / repo / asset validation",
      validTag("v0.2.1") && !validTag("v0.2.1-rc.1") && !validTag("main") && validSha(SHA) && !validSha("abc") &&
        validRepository("o/r") && !validRepository("a/b/c") &&
        assetFiles(["x.snupkg", "README.md", "x.nupkg"]).join("|") === "x.nupkg|x.snupkg" &&
        uploadUrl("https://u/r/1/assets{?name,label}", "A b.nupkg") === "https://u/r/1/assets?name=A%20b.nupkg");
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
  console.log(`selftest: ${passed}/${passed + failed} passed`);
  return failed === 0 ? 0 : 1;
}

// ---------------------------------------------------------------- main

if (import.meta.main) {
  const args = process.argv.slice(2);
  try {
    if (args.length === 1 && args[0] === "selftest") process.exit(await selftest());
    if ((args.length === 3 || args.length === 4) && (args.length === 3 || args[3] === "--dry-run")) {
      const [tag, sha, dir] = args as [string, string, string];
      const dryRun = args.length === 4;
      if (!validTag(tag)) throw new Refused(`not a release tag: '${tag}' (expected vX.Y.Z)`);
      if (!validSha(sha)) throw new Refused(`not a full commit sha: '${sha}'`);
      const repo = process.env.GITHUB_REPOSITORY ?? "";
      if (!validRepository(repo)) throw new Refused(`GITHUB_REPOSITORY must be owner/name, got '${repo}'`);
      const token = process.env.GITHUB_TOKEN;
      if (!dryRun && !token) throw new Refused("GITHUB_TOKEN is not set");
      if (!existsSync(dir)) throw new Refused(`no directory '${dir}'`);
      const api = new Api((u, i) => fetch(u, i), process.env.GITHUB_API_URL ?? "https://api.github.com", repo, dryRun ? undefined : token, CALL_SECONDS);
      await release(api, tag, sha, dir, dryRun, (l) => console.log(l));
      process.exit(0);
    }
    console.error("usage: github-release.ts <tag> <commit-sha> <dir> [--dry-run] | selftest");
    process.exit(2);
  } catch (e) {
    console.error(`github-release: ${(e as Error).message}`);
    process.exit(1);
  }
}
