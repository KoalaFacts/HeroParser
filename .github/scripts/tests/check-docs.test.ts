// Tests for .github/scripts/check-docs.ts. Run with `node --test .github/scripts/tests/`.

import assert from "node:assert/strict";
import { test } from "node:test";
import { checkDocs, collectAnchors, extractLinks, extractPathClaims, slugify } from "../check-docs.ts";

function run(files: Record<string, string>, extraPaths: string[] = []) {
  const paths = new Set([...Object.keys(files), ...extraPaths]);
  const directories = new Set<string>();
  for (const path of paths) {
    const parts = path.split("/");
    for (let i = 1; i < parts.length; i++) directories.add(parts.slice(0, i).join("/"));
  }
  return checkDocs({
    files: Object.keys(files),
    read: (path) => files[path],
    exists: (path) => paths.has(path) || directories.has(path),
    isDirectory: (path) => directories.has(path),
  });
}

const POINTER = "# Agent instructions\n\nSee [CLAUDE.md](CLAUDE.md).\n";

test("slugify follows GitHub heading anchors", () => {
  assert.equal(slugify("8.3 `Map<TProperty>()` inline mapping"), "83-maptproperty-inline-mapping");
  assert.equal(slugify("Delimiter Detection & CSV Validation"), "delimiter-detection--csv-validation");
  assert.equal(slugify("Excel (.xlsx) Reading"), "excel-xlsx-reading");
  // A raw generic outside code renders as an HTML tag, so it contributes nothing.
  assert.equal(slugify("8.3 Map<TProperty>() inline mapping"), "83-map-inline-mapping");
});

test("collectAnchors numbers repeated headings and ignores fenced code", () => {
  const anchors = collectAnchors("# Usage\n## Usage\n```\n# not a heading\n```\n<a id=\"custom\"></a>\n");
  assert.deepEqual([...anchors].sort(), ["custom", "usage", "usage-1"]);
});

test("extractLinks skips inline code and fenced code", () => {
  const links = extractLinks("[a](x.md) `[b](y.md)`\n```\n[c](z.md)\n```\n[ref]: w.md\n");
  assert.deepEqual(links.map((l) => l.target), ["x.md", "w.md"]);
});

test("extractPathClaims finds repository paths and skips placeholders", () => {
  const claims = extractPathClaims("Edit `src/HeroParser/Csv.cs`, not `src/<Name>/` or `src/**/*.cs` or `Csv.cs`.");
  assert.deepEqual(claims.map((c) => c.target), ["src/HeroParser/Csv.cs"]);
});

test("a consistent doc set passes", () => {
  const issues = run({
    "README.md": "# HeroParser\n\nSee [the CSV guide](docs/csv.md#reading).\n",
    "docs/csv.md": "# CSV\n\n## Reading\n\nSource: `src/HeroParser/Csv.cs`. Back to [README](../README.md).\n",
    "AGENTS.md": POINTER,
    "CLAUDE.md": "# Guidelines\n",
  }, ["src/HeroParser/Csv.cs"]);
  assert.deepEqual(issues, []);
});

test("reports a link to a missing file", () => {
  const issues = run({ "README.md": "[gone](docs/gone.md)\n" });
  assert.equal(issues.length, 1);
  assert.match(issues[0].message, /broken link 'docs\/gone\.md'/);
});

test("reports a link to a missing anchor", () => {
  const issues = run({ "README.md": "[x](docs/a.md#nope)\n", "docs/a.md": "# A\n" });
  assert.equal(issues.length, 1);
  assert.match(issues[0].message, /broken anchor/);
});

test("reports a same-page anchor that does not exist", () => {
  const issues = run({ "README.md": "# Top\n[x](#top) [y](#bottom)\n" });
  assert.deepEqual(issues.map((i) => i.message), ["broken anchor '#bottom': no heading '#bottom' in README.md"]);
});

test("reports a docs page that the README does not reach", () => {
  const issues = run({ "README.md": "# HeroParser\n", "docs/orphan.md": "# Orphan\n" });
  assert.deepEqual(issues.map((i) => `${i.file}: ${i.message}`), ["docs/orphan.md: not reachable from the README.md docs index"]);
});

test("reaches docs transitively", () => {
  const issues = run({ "README.md": "[a](docs/a.md)\n", "docs/a.md": "[b](perf/b.md)\n", "docs/perf/b.md": "# B\n" });
  assert.deepEqual(issues, []);
});

test("reports a named path that does not exist, except in historical records", () => {
  const issues = run({
    "README.md": "[c](CHANGELOG.md) [p](docs/perf/old.md)\nSee `src/Removed/Thing.cs`.\n",
    "CHANGELOG.md": "Removed `src/Removed/Thing.cs`.\n",
    "docs/perf/old.md": "Measured `benchmarks/Gone/Probe.cs`.\n",
  });
  assert.deepEqual(issues.map((i) => `${i.file}:${i.line}`), ["README.md:2"]);
  assert.match(issues[0].message, /named path 'src\/Removed\/Thing\.cs' does not exist/);
});

test("reports links that leave the repository", () => {
  const issues = run({ "README.md": "[up](../outside.md)\n" });
  assert.match(issues[0].message, /leaves the repository/);
});

test("ignores external links", () => {
  assert.deepEqual(run({ "README.md": "[x](https://example.com/a.md#b) [m](mailto:a@b.c)\n" }), []);
});

test("AGENTS.md must be a short pointer that links CLAUDE.md", () => {
  const copy = run({ "README.md": "# R\n", "CLAUDE.md": "# G\n", "AGENTS.md": "# Copy\n" + "guidance line\n".repeat(200) });
  assert.deepEqual(copy.map((i) => i.file), ["AGENTS.md", "AGENTS.md"]);
  assert.match(copy[0].message, /short pointer/);
  assert.match(copy[1].message, /must link to CLAUDE\.md/);
  assert.deepEqual(run({ "README.md": "# R\n", "CLAUDE.md": "# G\n", "AGENTS.md": POINTER }), []);
});

test("skill copies under .agents and .claude must match", () => {
  const base = { "README.md": "# R\n" };
  assert.deepEqual(run({ ...base, ".agents/skills/x/SKILL.md": "a\n", ".claude/skills/x/SKILL.md": "a\n" }), []);
  const differ = run({ ...base, ".agents/skills/x/SKILL.md": "a\n", ".claude/skills/x/SKILL.md": "b\n" });
  assert.match(differ[0].message, /differs from \.claude\/skills\/x\/SKILL\.md/);
  const missing = run({ ...base, ".agents/skills/y/SKILL.md": "a\n" });
  assert.match(missing[0].message, /has no \.claude\/skills\/y\/SKILL\.md counterpart/);
});
