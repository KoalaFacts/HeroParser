// Tests for .github/scripts/check-doc-snippets.ts. Run with `node --test .github/scripts/tests/check-doc-snippets.test.ts`.
// The compile step itself runs in CI (Docs Check job) against the real pages.

import assert from "node:assert/strict";
import { test } from "node:test";
import { extractSnippets, generatePage, pageNamespace, paragraphs, parseBuildErrors, splitSnippet } from "../check-doc-snippets.ts";

const fence = "```";

test("extracts csharp and cs blocks with their first code line", () => {
  const page = extractSnippets(`# T\n\n${fence}csharp\nvar a = 1;\n${fence}\n\n${fence}bash\nls\n${fence}\n${fence}cs\nvar b = 2;\n${fence}\n`);
  assert.deepEqual(page.snippets.map((s) => [s.line, s.code]), [[4, ["var a = 1;"]], [11, ["var b = 2;"]]]);
});

test("reads skip, alternatives and setup markers for the next block only", () => {
  const page = extractSnippets([
    "<!-- snippet: skip shows a fragment -->",
    `${fence}csharp`, ".WithX()", fence,
    "<!-- snippet: setup", "var input = \"\";", "-->",
    "<!-- snippet: alternatives -->",
    `${fence}csharp`, "var a = input;", fence,
    `${fence}csharp`, "var c = 3;", fence,
  ].join("\n"));
  assert.equal(page.snippets[0].skip, "shows a fragment");
  assert.deepEqual(page.snippets[1].setup, { line: 6, code: ["var input = \"\";"] });
  assert.equal(page.snippets[1].alternatives, true);
  assert.equal(page.snippets[2].skip, undefined);
  assert.equal(page.snippets[2].setup, undefined);
  assert.equal(page.snippets[2].alternatives, undefined);
});

test("reads one page setup", () => {
  const page = extractSnippets(`<!-- snippet: page-setup\nstatic string csv = "";\n-->\n${fence}csharp\nx();\n${fence}\n`);
  assert.deepEqual(page.pageSetup, { line: 2, code: ['static string csv = "";'] });
});

test("rejects markers that are malformed or misplaced", () => {
  assert.throws(() => extractSnippets("<!-- snippet: skip -->\n```csharp\nx\n```"), /needs a reason/);
  assert.throws(() => extractSnippets("<!-- snippet: setup\nx;\n"), /unterminated/);
  assert.throws(() => extractSnippets("<!-- snippet: skip why -->\nSome prose.\n```csharp\nx\n```"), /directly above a code block/);
  assert.throws(() => extractSnippets("<!-- snippet: skip why -->\n```bash\nls\n```"), /only valid above a csharp block/);
  assert.throws(() => extractSnippets("<!-- snippet: compile-later -->\n```csharp\nx\n```"), /unknown snippet marker/);
  assert.throws(() => extractSnippets("<!-- snippet: page-setup\n-->\n<!-- snippet: page-setup\n-->\n"), /only one/);
});

test("splits usings, type declarations and statements", () => {
  const split = splitSnippet([
    "using HeroParser;",
    "using var reader = Csv.ReadFromText(text);",
    "",
    "[GenerateBinder]",
    "public class Trade",
    "{",
    "    public string Symbol { get; set; } = \"}\"; // }",
    "}",
    "public record Quote(string Symbol);",
    "foreach (var row in reader) { }",
    "namespace MyApp;",
  ], 10);
  assert.deepEqual(split.usings, [{ line: 10, lines: ["using HeroParser;"] }]);
  assert.deepEqual(split.declarations.map((d) => [d.name, d.segment.line, d.segment.lines.length]), [["Trade", 13, 5], ["Quote", 18, 1]]);
  assert.deepEqual(split.statements.map((s) => [s.line, s.lines]), [
    [11, ["using var reader = Csv.ReadFromText(text);", ""]],
    [19, ["foreach (var row in reader) { }"]],
  ]);
});

test("an attribute on a statement stays a statement", () => {
  const split = splitSnippet(["[Obsolete] static void Local() { }", "Local();"], 1);
  assert.equal(split.declarations.length, 0);
  assert.equal(split.statements[0].lines.length, 2);
});

test("paragraphs split at blank lines and keep line numbers", () => {
  assert.deepEqual(paragraphs([{ line: 5, lines: ["// a", "var x = 1;", "", "", "var x = 2;"] }]), [
    { line: 5, lines: ["// a", "var x = 1;"] },
    { line: 9, lines: ["var x = 2;"] },
  ]);
});

test("generates one file per block, nested namespaces, #line mapping and hoisted first declarations", () => {
  const page = extractSnippets([
    "<!-- snippet: page-setup", "static string csv = \"\";", "-->",
    `${fence}csharp`, "using HeroParser;", "var t = new Trade();", fence,
    `${fence}csharp`, "public class Trade { }", fence,
    `${fence}csharp`, "public class Trade { public int Id; }", "var again = new Trade().Id;", fence,
  ].join("\n"), "docs/page.md");
  const files = generatePage("docs/page.md", page, "/repo/docs/page.md");
  assert.deepEqual(files.map((f) => f.name), ["Doc_docs_page_Page.cs", "Doc_docs_page_B001.cs", "Doc_docs_page_B002.cs", "Doc_docs_page_B003.cs"]);
  const [root, first, second, third] = files.map((f) => f.content);
  // The first Trade is visible to block 1, which uses it above its declaration.
  assert.match(root, /namespace Doc_docs_page\n\{\n#line 9 "\/repo\/docs\/page.md"\npublic class Trade \{ \}/);
  assert.match(root, /internal static string csv = "";/);
  assert.doesNotMatch(second, /class Trade/);
  // The redeclaration stays in block 3's namespace, nested in block 2's.
  assert.match(third, /namespace Doc_docs_page\.B001\.B002\.B003\n\{\n#line 12 "\/repo\/docs\/page.md"\npublic class Trade \{ public int Id; \}/);
  // Usings accumulate down the page; statements are mapped to their Markdown line.
  assert.match(third, /^#line 5 "\/repo\/docs\/page.md"\nusing HeroParser;/m);
  assert.match(first, /class Snippet : global::Doc_docs_page\.DocPage/);
  assert.match(first, /#line 6 "\/repo\/docs\/page.md"\nvar t = new Trade\(\);/);
});

test("alternatives get a scope per paragraph; skipped blocks generate nothing", () => {
  const page = extractSnippets([
    "<!-- snippet: alternatives -->",
    `${fence}csharp`, "var x = 1;", "", "var x = 2;", fence,
    "<!-- snippet: skip fragment -->",
    `${fence}csharp`, ".WithX()", fence,
  ].join("\n"));
  const files = generatePage("p.md", page);
  assert.equal(files.length, 2);
  assert.match(files[1].content, /\{\n#line 3 "p.md"\nvar x = 1;\n\}\n\{\n#line 5 "p.md"\nvar x = 2;\n\}/);
});

test("page namespaces are valid identifiers", () => {
  assert.equal(pageNamespace("docs/fixed-width.md"), "Doc_docs_fixed_width");
  assert.equal(pageNamespace("README.md"), "Doc_README");
});

test("parses and de-duplicates compiler errors", () => {
  const output = [
    "/repo/docs/csv.md(12,5): error CS0103: The name 'csv' does not exist in the current context [/tmp/x/DocSnippets.csproj]",
    "/repo/docs/csv.md(12,5): error CS0103: The name 'csv' does not exist in the current context [/tmp/x/DocSnippets.csproj]",
    "/repo/docs/csv.md(3,1): warning CS0168: unused",
    "Build FAILED.",
  ].join("\n");
  assert.deepEqual(parseBuildErrors(output), ["/repo/docs/csv.md(12,5): error CS0103: The name 'csv' does not exist in the current context"]);
});
