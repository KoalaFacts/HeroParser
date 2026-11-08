// Compiles the ```csharp blocks of README.md and docs/*.md against the repository's projects,
// so documented examples keep compiling as the API changes.
//
// Run from the repository root with Node 24+ and the .NET SDK on PATH:
//   node .github/scripts/check-doc-snippets.ts [--keep] [file.md ...]
//
// How a page becomes code. Every block becomes its own C# file in one scratch project:
//   * `using` directives accumulate down the page, as if the reader kept one file open;
//   * type declarations (classes, records, ...) go at namespace level, statements into an async
//     method, so `await` and `await foreach` work and each block has its own local variables;
//   * the first declaration of each type name on a page is visible to every block (C# allows a type
//     to be used above its declaration); a later declaration with the same name lives in its block's
//     namespace, which is nested inside the previous block's, so it shadows the first one for the
//     blocks below it instead of clashing;
//   * `#line` directives map every compiler error back to the Markdown file and line.
//
// Markers, written on the line(s) directly above a block:
//   <!-- snippet: skip <reason> -->   the block is deliberately partial; it is not compiled.
//   <!-- snippet: alternatives -->    the block's blank-line-separated paragraphs are alternative
//                                     ways to do one thing (each may declare `var orders`); each
//                                     paragraph is compiled in its own scope.
//   <!-- snippet: setup
//   <C# statements or declarations>
//   -->                               hidden code the reader is expected to supply (an input
//                                     variable, a helper method). Statements are prepended to the
//                                     block's method; type declarations are added to its namespace.
// And once per page, anywhere:
//   <!-- snippet: page-setup
//   <usings, type declarations, static fields and methods>
//   -->                               hidden context shared by every block of the page: the record
//                                     types the page assumes, input streams, helpers. Types go in
//                                     the page's root namespace; static members are inherited by
//                                     every block, and a block's own local of the same name wins.
// A skip marker needs a reason. Blocks are never deleted to make this check pass.

import { execFileSync, spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

export interface Snippet {
  /** 1-based line of the first code line (the line after the opening fence). */
  line: number;
  code: string[];
  skip?: string;
  alternatives?: boolean;
  setup?: { line: number; code: string[] };
}

export interface Page {
  snippets: Snippet[];
  pageSetup?: { line: number; code: string[] };
}

export interface SourceSegment {
  /** 1-based Markdown line of the segment's first line. */
  line: number;
  lines: string[];
}

export interface SplitSnippet {
  usings: SourceSegment[];
  types: SourceSegment[];
  /** Each type declaration with its name, in order. */
  declarations: { name: string; segment: SourceSegment }[];
  statements: SourceSegment[];
}

const FENCE = /^(\s*)(`{3,}|~{3,})\s*([\w#+-]*)/;
const SETUP_OPEN = /^\s*<!--\s*snippet:\s*setup\s*$/;
const PAGE_SETUP_OPEN = /^\s*<!--\s*snippet:\s*page-setup\s*$/;
// HTML ends a comment at "-->" and also at "--!>" (a parse error browsers accept), so both close a marker.
const COMMENT_END = /^\s*--!?>\s*$/;
const SKIP = /^\s*<!--\s*snippet:\s*skip\b\s*(.*?)\s*--!?>\s*$/;
const ALTERNATIVES = /^\s*<!--\s*snippet:\s*alternatives\s*--!?>\s*$/;
const MARKER = /^\s*<!--\s*snippet:/;

/** The ```csharp / ```cs blocks of a Markdown page with their markers. */
export function extractSnippets(markdown: string, file = "page.md"): Page {
  const lines = markdown.split(/\r?\n/);
  const snippets: Snippet[] = [];
  let pageSetup: Page["pageSetup"];
  let pendingSkip: string | undefined;
  let pendingAlternatives = false;
  let pendingSetup: Snippet["setup"];
  for (let i = 0; i < lines.length; i++) {
    const text = lines[i];
    const skip = SKIP.exec(text);
    if (skip) {
      if (skip[1] === "") throw new Error(`${file}:${i + 1}: a snippet skip marker needs a reason`);
      pendingSkip = skip[1];
      continue;
    }
    if (ALTERNATIVES.test(text)) {
      pendingAlternatives = true;
      continue;
    }
    if (PAGE_SETUP_OPEN.test(text)) {
      if (pageSetup) throw new Error(`${file}:${i + 1}: only one snippet page-setup marker per page`);
      const start = i + 1;
      const code: string[] = [];
      for (i++; i < lines.length && !COMMENT_END.test(lines[i]); i++) code.push(lines[i]);
      if (i >= lines.length) throw new Error(`${file}:${start}: unterminated snippet page-setup marker`);
      pageSetup = { line: start + 1, code };
      continue;
    }
    if (SETUP_OPEN.test(text)) {
      const start = i + 1;
      const code: string[] = [];
      for (i++; i < lines.length && !COMMENT_END.test(lines[i]); i++) code.push(lines[i]);
      if (i >= lines.length) throw new Error(`${file}:${start}: unterminated snippet setup marker`);
      pendingSetup = { line: start + 1, code };
      continue;
    }
    if (MARKER.test(text)) throw new Error(`${file}:${i + 1}: unknown snippet marker '${text.trim()}'`);
    const fence = FENCE.exec(text);
    if (!fence) {
      if (text.trim() !== "" && (pendingSkip !== undefined || pendingSetup || pendingAlternatives)) {
        throw new Error(`${file}:${i + 1}: a snippet marker must be directly above a code block`);
      }
      continue;
    }
    const marker = fence[2];
    const language = fence[3].toLowerCase();
    const start = i + 1;
    const code: string[] = [];
    for (i++; i < lines.length; i++) {
      const close = /^\s*(`{3,}|~{3,})\s*$/.exec(lines[i]);
      if (close && close[1][0] === marker[0] && close[1].length >= marker.length) break;
      code.push(lines[i]);
    }
    if (language === "csharp" || language === "cs" || language === "c#") {
      snippets.push({ line: start + 1, code, skip: pendingSkip, alternatives: pendingAlternatives || undefined, setup: pendingSetup });
    } else if (pendingSkip !== undefined || pendingSetup || pendingAlternatives) {
      throw new Error(`${file}:${start}: a snippet marker is only valid above a csharp block`);
    }
    pendingSkip = undefined;
    pendingSetup = undefined;
    pendingAlternatives = false;
  }
  return { snippets, pageSetup };
}

const USING_DIRECTIVE = /^(global\s+)?using\s+(static\s+)?(\w+\s*=\s*)?[\w.]+(<[^;]*>)?\s*;\s*(\/\/.*)?$/;
const NAMESPACE_DECLARATION = /^namespace\s+[\w.]+\s*;\s*$/;
const MODIFIERS = "(?:(?:public|internal|private|protected|file|static|sealed|abstract|partial|readonly|ref|unsafe|new)\\s+)*";
const TYPE_START = new RegExp(`^${MODIFIERS}(?:class|record|struct|interface|enum|delegate)\\b`);
const ATTRIBUTE = /^\[[^\]]/;

/** Splits a block's lines into using directives, type declarations and statements. */
export function splitSnippet(code: string[], firstLine: number): SplitSnippet {
  const result: SplitSnippet = { usings: [], types: [], statements: [], declarations: [] };
  const add = (target: SourceSegment[], index: number, line: string) => {
    const last = target[target.length - 1];
    if (last && last.line + last.lines.length === firstLine + index) last.lines.push(line);
    else target.push({ line: firstLine + index, lines: [line] });
  };
  for (let i = 0; i < code.length; i++) {
    const line = code[i];
    if (USING_DIRECTIVE.test(line)) {
      add(result.usings, i, line);
      continue;
    }
    if (NAMESPACE_DECLARATION.test(line)) continue;
    if (ATTRIBUTE.test(line) || TYPE_START.test(line)) {
      // Attributes directly above a type belong to it; otherwise they are statements' business.
      const start = i;
      let j = i;
      while (j < code.length && ATTRIBUTE.test(code[j]) && !TYPE_START.test(code[j])) j++;
      if (j < code.length && TYPE_START.test(code[j])) {
        const end = findDeclarationEnd(code, j);
        for (let k = start; k <= end; k++) add(result.types, k, code[k]);
        const name = /\b(?:class|record|struct|interface|enum)\s+(?:class\s+|struct\s+)?(\w+)|\bdelegate\s+[\w<>,?\s[\]]+?\s(\w+)\s*[<(]/.exec(code[j]);
        result.declarations.push({ name: name?.[1] ?? name?.[2] ?? "", segment: { line: firstLine + start, lines: code.slice(start, end + 1) } });
        i = end;
        continue;
      }
    }
    add(result.statements, i, line);
  }
  return result;
}

/** Index of the last line of the type declaration starting at `start` (brace matched, or `;`). */
function findDeclarationEnd(code: string[], start: number): number {
  let depth = 0;
  let opened = false;
  for (let i = start; i < code.length; i++) {
    const text = code[i].replace(/"(?:[^"\\]|\\.)*"/g, '""').replace(/'(?:[^'\\]|\\.)'/g, "''").replace(/\/\/.*$/, "");
    for (const char of text) {
      if (char === "{") {
        depth++;
        opened = true;
      } else if (char === "}") {
        depth--;
      }
    }
    if (opened && depth <= 0) return i;
    if (!opened && /;\s*$/.test(text)) return i;
  }
  return code.length - 1;
}

function lineDirective(line: number, file: string): string {
  return `#line ${line} "${file.replace(/\\/g, "/")}"`;
}

function emitSegments(segments: SourceSegment[], file: string): string[] {
  return segments.flatMap((segment) => [lineDirective(segment.line, file), ...segment.lines]);
}

/** Splits segments at blank lines into paragraphs. */
export function paragraphs(segments: SourceSegment[]): SourceSegment[] {
  const result: SourceSegment[] = [];
  for (const segment of segments) {
    let current: SourceSegment | undefined;
    segment.lines.forEach((text, offset) => {
      if (text.trim() === "") {
        current = undefined;
        return;
      }
      if (!current) {
        current = { line: segment.line + offset, lines: [] };
        result.push(current);
      }
      current.lines.push(text);
    });
  }
  return result;
}

/** Statements of a block; alternatives get one `{ }` scope per paragraph. */
function emitStatements(segments: SourceSegment[], alternatives: boolean, file: string): string[] {
  if (!alternatives) return emitSegments(segments, file);
  return paragraphs(segments).flatMap((paragraph) => ["{", ...emitSegments([paragraph], file), "}"]);
}

/** A C# identifier for the page, used as its root namespace. */
export function pageNamespace(file: string): string {
  return "Doc_" + file.replace(/\.md$/i, "").replace(/[^A-Za-z0-9]/g, "_");
}

export interface GeneratedFile {
  name: string;
  content: string;
}

/**
 * One C# file per compiled block of a page, plus one for its page setup. `file` names the page
 * (and its namespace); `sourcePath` is the path compiler errors should name, by default `file`.
 */
export function generatePage(file: string, page: Page, sourcePath = file): GeneratedFile[] {
  const generated: GeneratedFile[] = [];
  const root = pageNamespace(file);
  const pageSetup = page.pageSetup ? splitSnippet(page.pageSetup.code, page.pageSetup.line) : undefined;
  const blocks = page.snippets.map((snippet, index) => ({
    snippet,
    index,
    split: snippet.skip === undefined ? splitSnippet(snippet.code, snippet.line) : undefined,
    setup: snippet.skip === undefined && snippet.setup ? splitSnippet(snippet.setup.code, snippet.setup.line) : undefined,
  }));

  // The first declaration of each name goes to the page's root namespace, visible to every block.
  const declared = new Set<string>((pageSetup?.declarations ?? []).map((d) => d.name));
  const hoisted: SourceSegment[] = [];
  const hoistedLines = new Set<number>();
  for (const block of blocks) {
    for (const declaration of [...(block.setup?.declarations ?? []), ...(block.split?.declarations ?? [])]) {
      if (declaration.name === "" || declared.has(declaration.name)) continue;
      declared.add(declaration.name);
      hoisted.push(declaration.segment);
      hoistedLines.add(declaration.segment.line);
    }
  }
  const unhoisted = (split: SplitSnippet | undefined) =>
    (split?.declarations ?? []).filter((d) => !hoistedLines.has(d.segment.line)).map((d) => d.segment);
  const allUsings = [
    ...(pageSetup?.usings ?? []),
    ...blocks.flatMap((block) => [...(block.setup?.usings ?? []), ...(block.split?.usings ?? [])]),
  ];

  generated.push({
    name: `${root}_Page.cs`,
    content: [
      "// <auto-generated/> page setup and first type declarations from " + file,
      ...emitSegments(allUsings, sourcePath),
      "#line default",
      `namespace ${root}`,
      "{",
      ...emitSegments([...(pageSetup?.types ?? []), ...hoisted], sourcePath),
      "#line default",
      "    internal class DocPage",
      "    {",
      // Members default to private; make them visible to the blocks that inherit them.
      ...emitSegments(
        (pageSetup?.statements ?? []).map((segment) => ({
          line: segment.line,
          lines: segment.lines.map((line) => line.replace(/^(static|const)\b/, "internal $1")),
        })),
        sourcePath,
      ),
      "#line default",
      "    }",
      "}",
      "",
    ].join("\n"),
  });

  const usings: SourceSegment[] = [...(pageSetup?.usings ?? [])];
  let namespace = root;
  for (const { snippet, index, split, setup } of blocks) {
    if (split === undefined) continue;
    usings.push(...(setup?.usings ?? []), ...split.usings);
    const suffix = `B${String(index + 1).padStart(3, "0")}`;
    namespace = `${namespace}.${suffix}`;
    const content = [
      "// <auto-generated/> from " + file,
      ...emitSegments(usings, sourcePath),
      "#line default",
      `namespace ${namespace}`,
      "{",
      ...emitSegments([...unhoisted(setup), ...unhoisted(split)], sourcePath),
      "#line default",
      `    internal sealed class Snippet : global::${root}.DocPage`,
      "    {",
      "        internal static async System.Threading.Tasks.Task RunAsync()",
      "        {",
      "            await System.Threading.Tasks.Task.CompletedTask;",
      ...emitSegments(setup?.statements ?? [], sourcePath),
      ...emitStatements(split.statements, snippet.alternatives === true, sourcePath),
      "#line default",
      "        }",
      "    }",
      "}",
      "",
    ].join("\n");
    generated.push({ name: `${root}_${suffix}.cs`, content });
  }
  return generated;
}

/** Compiler errors from `dotnet build` output, de-duplicated (the build prints them twice). */
export function parseBuildErrors(output: string): string[] {
  const errors = new Set<string>();
  for (const line of output.split(/\r?\n/)) {
    const match = /^\s*(.+?\(\d+,\d+\)): error ([A-Z]+\d+): (.*?)(?: \[[^\]]*\])?$/.exec(line);
    if (match) errors.add(`${match[1]}: error ${match[2]}: ${match[3]}`);
  }
  return [...errors];
}

function projectFile(root: string): string {
  const reference = (path: string, extra = "") => `    <ProjectReference Include="${join(root, path)}"${extra} />`;
  return [
    '<Project Sdk="Microsoft.NET.Sdk">',
    "  <PropertyGroup>",
    "    <TargetFramework>net10.0</TargetFramework>",
    "    <OutputType>Library</OutputType>",
    "    <ImplicitUsings>enable</ImplicitUsings>",
    "    <Nullable>enable</Nullable>",
    "    <LangVersion>14.0</LangVersion>",
    "    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>",
    "    <!-- Examples are fragments: unused locals, unawaited calls and nullability are not errors here. -->",
    "    <WarningLevel>0</WarningLevel>",
    "    <EnableNETAnalyzers>false</EnableNETAnalyzers>",
    "  </PropertyGroup>",
    "  <ItemGroup>",
    reference("src/HeroParser/HeroParser.csproj"),
    reference("src/HeroParser.Console/HeroParser.Console.csproj"),
    reference("src/HeroParser.Generators/HeroParser.Generators.csproj", ' OutputItemType="Analyzer" ReferenceOutputAssembly="false"'),
    "  </ItemGroup>",
    "</Project>",
    "",
  ].join("\n");
}

export function defaultPages(root: string): string[] {
  const docs = execFileSync("git", ["ls-files", "docs/*.md"], { cwd: root, encoding: "utf8" })
    .split(/\r?\n/)
    .filter((file) => file !== "" && !file.slice("docs/".length).includes("/"));
  return ["README.md", ...docs];
}

function main(): void {
  const root = process.cwd();
  const args = process.argv.slice(2);
  const keep = args.includes("--keep");
  const pages = args.filter((arg) => !arg.startsWith("--"));
  const files = pages.length > 0 ? pages : defaultPages(root);

  const work = mkdtempSync(join(tmpdir(), "heroparser-doc-snippets-"));
  const sources = join(work, "Snippets");
  mkdirSync(sources);
  let compiled = 0;
  let skipped = 0;
  try {
    for (const file of files) {
      const page = extractSnippets(readFileSync(join(root, file), "utf8"), file);
      skipped += page.snippets.filter((s) => s.skip !== undefined).length;
      compiled += page.snippets.length - page.snippets.filter((s) => s.skip !== undefined).length;
      for (const generated of generatePage(file, page, resolve(root, file))) {
        writeFileSync(join(sources, generated.name), generated.content);
      }
    }
    writeFileSync(join(work, "DocSnippets.csproj"), projectFile(root));
    writeFileSync(join(work, "global.json"), readFileSync(join(root, "global.json")));
    const build = spawnSync("dotnet", ["build", join(work, "DocSnippets.csproj"), "-c", "Release", "-nologo", "-clp:NoSummary"], {
      cwd: work,
      encoding: "utf8",
      maxBuffer: 64 * 1024 * 1024,
    });
    const output = `${build.stdout ?? ""}${build.stderr ?? ""}`;
    const errors = parseBuildErrors(output).map((error) => error.replace(`${root.replace(/\\/g, "/")}/`, ""));
    if (build.status !== 0) {
      for (const error of errors) console.error(error);
      if (errors.length === 0) console.error(output);
      console.error(`check-doc-snippets: ${errors.length} compile error(s) in ${compiled} snippets from ${files.length} pages`);
      process.exitCode = 1;
      return;
    }
    console.log(`check-doc-snippets: ${compiled} snippets from ${files.length} pages compile (${skipped} marked as partial)`);
  } finally {
    if (keep) console.log(`check-doc-snippets: generated project kept at ${work}`);
    else rmSync(work, { recursive: true, force: true });
  }
}

if (import.meta.main) main();
