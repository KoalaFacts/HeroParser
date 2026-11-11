---
name: advance-heroparser
description: How the scheduled task advances HeroParser — goal, gate, work order, scope and project rules. Read with agent-harness/skills/autonomous-dev at the start of every run.
---

# Advance HeroParser

This skill is the repo-specific half of the "HeroParser 推进计划" scheduled task; the run loop is `agent-harness/skills/autonomous-dev` and the owner's seed is `agent-harness/projects/heroparser.md`. Where this file and the seed differ, the stricter wins. It never relaxes the hard limits in the scheduled task's prompt.

## Goal and metric
- HeroParser: a .NET CSV / Fixed-Width / Excel / JSONL / HTB parser library with a source generator, a console engine, a CLI, a WebAssembly npm package and a browser demo.
- Goal: .NET 11 readiness before .NET 11 GA, all dependencies current, and docs that are always true, tidy and organised. **Preparation only, no release.**
- Headline metric: ledger items done, plus the local gate's test counts (HeroParser.Tests / Generators.Tests / Console.Tests on net10.0 and net11.0) on the merged commit.
- Real progress: a merged PR that changes working code and tests (a fix with a test that failed on `main` for the intended reason), or a ledger item delivered with its own evidence.

## Setup
- Read CLAUDE.md (AGENTS.md is a pointer to it), CHANGELOG.md, `.github/workflows/README.md`, `verify-heroparser`, and the bodies of the task's last few PRs (the run log).
- SDKs into one scratch folder, first on PATH: `dotnet-install.sh --channel 11.0 --quality preview` and `--channel 10.0`, then `dotnet workload install wasm-tools wasm-tools-net10` (the Wasm project targets net10.0; the solution does not build without it). Add 8.0/9.0 runtimes only when needed.
- Node 24+ and npm 12 (`npm install --prefix <scratch> npm@12`).

## Gate
In order; warnings are errors.
1. `dotnet restore HeroParser.slnx --force-evaluate`
2. `dotnet build HeroParser.slnx -c Release`
3. `dotnet format whitespace --verify-no-changes HeroParser.slnx`
4. HeroParser.Tests, HeroParser.Generators.Tests and HeroParser.Console.Tests on net10.0 and net11.0: `dotnet test --project <proj> -c Release -f <tfm>`
5. AotTests: `dotnet run` on net11.0, plus `dotnet publish -f net11.0 -r linux-x64` and run the native binary; no IL/trim warnings.
6. When `npm/` or `src/HeroParser.Wasm/` changed: Wasm publish, `npm ci --prefix npm --legacy-peer-deps`, demo build, `vue-tsc -b`, `vp fmt --check`, `node npm/tests/test-wasm.js`, and a Playwright smoke test of the demo under `/HeroParser/demo/`.
7. Docs: `node .github/scripts/check-docs.ts` and `node .github/scripts/check-doc-snippets.ts`.

## Ledger
The "Maintenance ledger" section of CLAUDE.md (no separate ledger file exists; do not create one). Statuses: `todo`, `in progress (PR #n)`, `done (PR #n)`, `waiting (on what)`, `blocked (reason; owner decision)`. Update it in every PR. The dated run log lives in PR bodies; user-visible changes go in CHANGELOG `[Unreleased]` (Added / Changed / Fixed / Build and CI / Dependencies / Notes).

## Work order
- **L1** net11.0 target, `global.json` floor (rollForward latestFeature; allowPrerelease false since GA, see L4), CI on the newest 11.0 SDK via setup-dotnet `11.0.x` with no forced dotnet-quality, NuGet consumer check on net11.0, dependency upgrades (#167). Then close #162 as superseded.
- **L2** C# snippets in README.md and docs/*.md compile in CI (TypeScript tool, explicit marker for deliberate fragments). Never delete examples to pass.
- **L3** Historical-evidence job: owner decision (retire it, or report "evidence expired" as pass-with-notice). Options on #167 and in the ledger; do not change the job until the owner answers.
- **L4** After .NET 11 GA (`latest-release` without a prerelease suffix in https://builds.dotnet.microsoft.com/dotnet/release-metadata/11.0/releases.json): confirm CI installs the GA SDK, raise the `global.json` floor to GA (the 2.9.0 release gate), BenchmarkDotNet to the newest stable 0.16.x if any, drop "RC" wording. Until GA it is `waiting`, not blocked.
- **L5** .NET 11 analyzer and obsoletion sweep with `-p:AnalysisLevel=latest-all`: fix real issues with tests first, one fix per PR (L5a, L5b, ...); record intentionally ignored diagnostics with reasons in the ledger; suppress in code only as CLAUDE.md allows.
- **L6** Dependency upkeep between PRs: `dotnet list HeroParser.slnx package --outdated` (also `--include-prerelease`) and `npm outdated --workspaces --include-workspace-root` in `npm/`. Port breaking majors in `deps/` PRs preserving behaviour; shipped packages' runtime dependencies stay stable. Held back: `onnxruntime-web` 1.20.1 (override + postinstall patches), TypeScript 6.x until vue-tsc supports 7.
- **L7** Docs accuracy audit (README.md, docs/*.md, the npm package README, browser demo text, CLAUDE.md, `.github/workflows/README.md` against the code), one area per PR. The demo must keep working under `/HeroParser/demo/`.
- When the list is exhausted: the most valuable correctness, safety or ease-of-use gap, with the reason in the PR.

## Authority
- Branch prefixes `net11/`, `deps/`, `fix/`, `docs/`; squash merge pinned to the head SHA.
- First line of every PR body: "Opened by the HeroParser scheduled task."
- Also allowed: PR #167 (`feature/net11-support`, counts as this task's PR; others may push to it — fetch first, merge their commits in, never overwrite or revert their work without a traced reason) and green Dependabot patch/minor PRs with no findings, merged between PRs. Touch #162 only to close it as superseded after #167 merges.
- Merging: the main ruleset reports `current_user_can_bypass: always` for the task's token (ledger M1); merge when every merge condition holds. Every other open PR and stray branch (owner, Codex, Dependabot, other sessions) is handled only through the open-PR triage (autonomous-dev §3 step 0, the owner's standing instruction in the scheduled task): merge when ready, continue on its own branch when unfinished, close when superseded; never overwrite others' commits, and leave anything pushed by someone else in the last 90 minutes.
- Merging `main` deploys the browser demo (GitHub Pages); nothing else.

## Project rules
- **No release:** never dispatch or re-run Create Release or any Publish workflow (NuGet, WinGet, Homebrew, Scoop, Snap); never change `<Version>` in Directory.Build.props; never run `npm run sync-version`; never publish the npm package.
- The "Validate Historical Evidence And Optional Fixed Collection" job fails on any PR touching `csv-pipe-history.yml` because the artifact of run 36732093817 expired; it is not counted against a merge and is never "fixed" by a re-run.
- Codex reviews only when a PR is opened and the account is at its usage limit; do not wait for a new Codex review after a push. When Codex has not reviewed or reports it cannot, an independent review agent reviews the diff. No open blocking/high/security finding; every thread (including the owner's account) fixed or answered.
- Never claim performance without CLAUDE.md's same-runner A/B.
- Owner decisions the task must not take: L3; the npm package README (#172) waits for owner approval.

## Docs scope
- CLAUDE.md is the source of agent guidance; AGENTS.md is a pointer, enforced by `check-docs.ts`. `.agents/skills/` and `.claude/skills/` are identical copies, also enforced.
- `docs/perf/` notes are benchmark evidence and stay where they are (at most a one-line status note at the top); superseded non-evidence notes are folded into their owning page and deleted; no archive folder.
- Version wording stays "coming in 2.9.0" until released.

## Report extras
- .NET 11 status and the SDK CI used.
- Dependency changes and what is held back.
