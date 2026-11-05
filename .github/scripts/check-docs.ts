// Checks the repository's Markdown documentation:
//   * relative links point at files that exist, and #anchors at headings that exist;
//   * every page under docs/ is reachable from README.md, which is the docs index;
//   * repository paths named in inline code (`src/...`, `.github/...`) exist;
//   * AGENTS.md stays a short pointer to CLAUDE.md instead of a drifting copy;
//   * skills under .agents/skills/ are identical to their .claude/skills/ twins.
//
// Run from the repository root with Node 24+: `node .github/scripts/check-docs.ts`.
// Exits 1 and prints one line per problem when anything fails.

import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import { dirname, join, normalize, sep } from "node:path";

export interface DocIssue {
  file: string;
  line: number;
  message: string;
}

export interface MarkdownLink {
  target: string;
  line: number;
}

/** Markdown files whose content is a dated historical record: links are checked, path claims are not. */
const HISTORICAL_FILES = new Set(["CHANGELOG.md"]);
const HISTORICAL_PREFIXES = ["docs/perf/", "benchmarks/CsvPipeABProbe/"];

/** Top-level folders whose paths, when named in inline code, must exist. */
const PATH_ROOTS = ["src/", "tests/", "docs/", "benchmarks/", "npm/", "examples/", ".github/", "snap/"];

/** AGENTS.md must stay a pointer, not a second copy of CLAUDE.md. */
const AGENTS_MAX_BYTES = 2048;

/** GitHub's heading anchor: lowercase, punctuation dropped, spaces to hyphens. */
export function slugify(heading: string): string {
  // Code spans keep their text; raw HTML tags outside them render as nothing.
  const parts = heading.trim().split(/(`+[^`]*`+)/);
  return parts
    .map((part, index) => (index % 2 === 1 ? part.replace(/`/g, "") : part.replace(/<[^>]*>/g, "")))
    .join("")
    .toLowerCase()
    .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/[^\p{L}\p{N}\s_-]/gu, "")
    .replace(/\s/g, "-");
}

/** Lines outside fenced code blocks, with their 1-based numbers. */
function proseLines(markdown: string): { text: string; line: number }[] {
  const result: { text: string; line: number }[] = [];
  let fence: string | null = null;
  markdown.split(/\r?\n/).forEach((text, index) => {
    const marker = /^\s*(`{3,}|~{3,})/.exec(text);
    if (marker) {
      if (fence === null) fence = marker[1][0].repeat(marker[1].length);
      else if (marker[1].startsWith(fence)) fence = null;
      return;
    }
    if (fence === null) result.push({ text, line: index + 1 });
  });
  return result;
}

/** Anchors GitHub generates for the headings of a document, including -1, -2 suffixes for repeats. */
export function collectAnchors(markdown: string): Set<string> {
  const anchors = new Set<string>();
  const seen = new Map<string, number>();
  for (const { text } of proseLines(markdown)) {
    const heading = /^#{1,6}\s+(.*?)\s*#*\s*$/.exec(text);
    const explicit = text.matchAll(/<a\s+(?:name|id)="([^"]+)"/g);
    for (const match of explicit) anchors.add(match[1]);
    if (!heading) continue;
    const slug = slugify(heading[1]);
    const count = seen.get(slug) ?? 0;
    anchors.add(count === 0 ? slug : `${slug}-${count}`);
    seen.set(slug, count + 1);
  }
  return anchors;
}

function stripInlineCode(text: string): string {
  return text.replace(/(`+)[\s\S]*?\1/g, (code) => " ".repeat(code.length));
}

/** Inline and reference-style link targets outside code. */
export function extractLinks(markdown: string): MarkdownLink[] {
  const links: MarkdownLink[] = [];
  for (const { text, line } of proseLines(markdown)) {
    const prose = stripInlineCode(text);
    for (const match of prose.matchAll(/\]\(\s*<?([^)\s>]+)>?(?:\s+"[^"]*")?\s*\)/g)) {
      links.push({ target: match[1], line });
    }
    const reference = /^\s*\[[^\]]+\]:\s*<?(\S+?)>?(?:\s+".*")?\s*$/.exec(prose);
    if (reference) links.push({ target: reference[1], line });
    for (const match of prose.matchAll(/<(?:a|img)\s[^>]*(?:href|src)="([^"]+)"/g)) {
      links.push({ target: match[1], line });
    }
  }
  return links;
}

/** Repository paths named in inline code, such as `src/HeroParser/Csv.cs`. */
export function extractPathClaims(markdown: string): MarkdownLink[] {
  const claims: MarkdownLink[] = [];
  for (const { text, line } of proseLines(markdown)) {
    for (const match of text.matchAll(/`([^`\s]+)`/g)) {
      const value = match[1].replace(/[.,:;]$/, "");
      if (!PATH_ROOTS.some((root) => value.startsWith(root))) continue;
      if (/[*<>{}$?|\[\]]/.test(value)) continue;
      claims.push({ target: value, line });
    }
  }
  return claims;
}

function isExternal(target: string): boolean {
  return /^[a-z][a-z0-9+.-]*:/i.test(target) || target.startsWith("//");
}

function toPosix(path: string): string {
  return path.split(sep).join("/");
}

export interface DocsCheckInput {
  /** Repository-relative Markdown paths to check. */
  files: string[];
  /** Reads a repository-relative file. */
  read: (path: string) => string;
  /** Whether a repository-relative path exists (file or directory). */
  exists: (path: string) => boolean;
  /** Whether a repository-relative path is a directory. */
  isDirectory: (path: string) => boolean;
}

export function checkDocs(input: DocsCheckInput): DocIssue[] {
  const issues: DocIssue[] = [];
  const anchorCache = new Map<string, Set<string>>();
  const anchorsOf = (path: string): Set<string> => {
    let anchors = anchorCache.get(path);
    if (!anchors) {
      anchors = collectAnchors(input.read(path));
      anchorCache.set(path, anchors);
    }
    return anchors;
  };
  const graph = new Map<string, string[]>();

  for (const file of input.files) {
    const markdown = input.read(file);
    const outgoing: string[] = [];
    graph.set(file, outgoing);

    for (const link of extractLinks(markdown)) {
      if (isExternal(link.target)) continue;
      const [rawPath, anchor] = splitAnchor(link.target);
      let path: string;
      try {
        path = rawPath === "" ? file : toPosix(normalize(join(dirname(file), decodeURIComponent(rawPath))));
      } catch {
        issues.push({ file, line: link.line, message: `malformed link target '${link.target}'` });
        continue;
      }
      if (path.startsWith("..")) {
        issues.push({ file, line: link.line, message: `link '${link.target}' leaves the repository` });
        continue;
      }
      if (!input.exists(path)) {
        issues.push({ file, line: link.line, message: `broken link '${link.target}': ${path} does not exist` });
        continue;
      }
      if (path.endsWith(".md")) outgoing.push(path);
      if (anchor !== undefined && path.endsWith(".md") && !input.isDirectory(path)) {
        if (!anchorsOf(path).has(decodeURIComponent(anchor).toLowerCase())) {
          issues.push({ file, line: link.line, message: `broken anchor '${link.target}': no heading '#${anchor}' in ${path}` });
        }
      }
    }

    if (!isHistorical(file)) {
      for (const claim of extractPathClaims(markdown)) {
        if (!input.exists(claim.target.replace(/\/$/, ""))) {
          issues.push({ file, line: claim.line, message: `named path '${claim.target}' does not exist` });
        }
      }
    }
  }

  const reachable = new Set<string>();
  const queue = ["README.md"];
  while (queue.length > 0) {
    const current = queue.pop()!;
    if (reachable.has(current)) continue;
    reachable.add(current);
    queue.push(...(graph.get(current) ?? []));
  }
  for (const file of input.files) {
    if (file.startsWith("docs/") && !reachable.has(file)) {
      issues.push({ file, line: 1, message: "not reachable from the README.md docs index" });
    }
  }

  if (input.files.includes("AGENTS.md")) {
    const agents = input.read("AGENTS.md");
    if (Buffer.byteLength(agents, "utf8") > AGENTS_MAX_BYTES) {
      issues.push({ file: "AGENTS.md", line: 1, message: `must stay a short pointer to CLAUDE.md (at most ${AGENTS_MAX_BYTES} bytes); put guidance in CLAUDE.md` });
    }
    if (!extractLinks(agents).some((link) => splitAnchor(link.target)[0] === "CLAUDE.md")) {
      issues.push({ file: "AGENTS.md", line: 1, message: "must link to CLAUDE.md, the source of the agent guidance" });
    }
  }

  // Codex reads skills from .agents/skills, Claude from .claude/skills: the copies must match.
  for (const file of input.files) {
    if (!file.startsWith(".agents/skills/")) continue;
    const twin = `.claude/skills/${file.slice(".agents/skills/".length)}`;
    if (!input.files.includes(twin)) {
      issues.push({ file, line: 1, message: `has no ${twin} counterpart; keep the two skill folders identical` });
    } else if (input.read(file) !== input.read(twin)) {
      issues.push({ file, line: 1, message: `differs from ${twin}; keep the two skill folders identical` });
    }
  }

  return issues;
}

function isHistorical(file: string): boolean {
  return HISTORICAL_FILES.has(file) || HISTORICAL_PREFIXES.some((prefix) => file.startsWith(prefix));
}

function splitAnchor(target: string): [string, string | undefined] {
  const hash = target.indexOf("#");
  if (hash < 0) return [target.split("?")[0], undefined];
  return [target.slice(0, hash).split("?")[0], target.slice(hash + 1)];
}

function main(): void {
  const root = process.cwd();
  const files = execFileSync("git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard", "*.md"], { cwd: root, encoding: "utf8" })
    .split("\0")
    .filter((file) => file !== "" && !file.split("/").includes("node_modules") && existsSync(join(root, file)));
  const issues = checkDocs({
    files,
    read: (path) => readFileSync(join(root, path), "utf8"),
    exists: (path) => existsSync(join(root, path)),
    isDirectory: (path) => statSync(join(root, path)).isDirectory(),
  });
  for (const issue of issues) console.error(`${issue.file}:${issue.line}: ${issue.message}`);
  if (issues.length > 0) {
    console.error(`check-docs: ${issues.length} problem(s) in ${files.length} Markdown files`);
    process.exit(1);
  }
  console.log(`check-docs: ${files.length} Markdown files OK (links, anchors, index, named paths, agent instructions)`);
}

if (import.meta.main) main();
