# CSV Pipe: current correctness is not historical timing acceptance

> Historical implementation report. The experiment was subsequently [retired and archived](2026-10-25-csv-pipe-retirement.md); its live status reporting and manual timing entry below are no longer current policy. Original evidence is retained.

## Scope and authorization

On 2026-10-25 the maintainer explicitly requested this CI-policy correction after reviewing the historical gate. This is not a waiver of the historical performance result, an approval to merge, or authorization for new timing samples. The production parser, xUnit v3 4.0.2/Microsoft.Testing.Platform, .NET 8/9/10 target frameworks, benchmark workloads and sampling scripts are unchanged.

Audited parent: `26d2e7f315ee8af3418ad78cc30061a78d43557b` on PR #157. The old `CSV Pipe Correctness and Timing Controls` job in [run 40117178871](https://github.com/KoalaFacts/HeroParser/actions/runs/40117178871/job/120940753183) failed by throwing the recorded historical result before collecting any current timing samples. A red ordinary run therefore did not establish a regression in its current commit.

## Independent results

| Result | Ordinary PR/push policy |
| --- | --- |
| Current correctness | Run real CSV Pipe/PipeReader tests on every configured .NET 8/9/10 framework; fail for failing/skipped tests, zero tests, or missing/inconsistent TRX evidence. |
| Current reporting/control policy | Run the native TypeScript status/scope tests and existing synthetic PowerShell control validators; failures remain failures. |
| Current timing | `INCONCLUSIVE (not measured)`, zero new timing samples, no speedup claim. |
| Historical timing acceptance | Keep `FAILED`, original run/source, ratio, threshold and exhausted budget visible in every JSON and job summary. |

The new `CSV Pipe Correctness` matrix checks out and records the exact current source SHA. Its MTP command selects both `*CsvPipe*` and the core `HeroParser.Tests.PipeReaderTests` class with one multi-value `--filter-class` option, uses `--minimum-expected-tests 1` and `--fail-skips on`, and checks its TRX with the existing validator. Reporting and artifact upload run even on failure. The reporter requires success from policy tests, synthetic controls, build, tests and evidence validation: absent, skipped, cancelled or unknown prerequisites cannot become green. There is no `continue-on-error` workaround.

The existing timing job is now explicitly manual and named `CSV Pipe Timing Experiment (manual)`. Its body is unchanged, including the frozen-source diff check, unchanged-workload rejection, suspended A/B and fixed budget. This change does not dispatch it. Ordinary throughput/comparison jobs and all other diagnostic jobs are unchanged. Workflow concurrency keeps the existing grouping but sets `cancel-in-progress: false`, so a new revision queues behind an active capture instead of cancelling it. No workflow is manually restarted. They do not change the frozen experiment's status.

Historical reference: [run 36732093817](https://github.com/KoalaFacts/HeroParser/actions/runs/36732093817), source `89c06810e76c4623ebad3cfc89c4bcdef41acd59`, Segmented128 same-source ratio **1.052845684 > 1.05**, remaining sampling budget **0**. No historical evidence, threshold or failed sample is removed or reclassified. A genuinely new performance hypothesis still needs a separately authorized, predeclared experiment and valid controls; correctness CI does not grant that authorization.

## Branch-rule audit

At the audit, active main ruleset `10187148` required one PR approval and Code Owner review, and prohibited deletion/non-fast-forward updates. It did not list required status checks. Legacy branch-protection status contexts were empty. Thus the earlier assertion that this job was necessarily a required-status-check merge blocker was not established by the actual configuration. This patch does not alter any ruleset, protection, approval, review or merge state; these must be checked again before any merge.

## Local verification and limits

Native TypeScript policy tests were run with Node **v22.16.0** using:

```sh
node --experimental-strip-types --test benchmarks/CsvPipeABProbe/test-ci-status.ts
```

The same final test file against the unmodified parent workflow, without the new helper, exited **1**: **24 total, 1 passed, 23 failed, 0 skipped**. With the implementation it exited **0**: **24 passed, 0 failed, 0 skipped**. Tests execute the checked-in job condition and the actual summary script with controlled IO/outcomes; both successful and failed-test paths write independent current/historical evidence and propagate failure. YAML parsing and structural comparison confirmed every other job unchanged, the historical timing job changed only in its name/condition, and the root concurrency change only disables cancellation. A separate regression fails before that queue-policy correction and passes afterward.

CI uses the already pinned `actions/github-script` v9 runtime (Node 24) to run the `.ts` tests directly, with no new package/toolchain dependency. The local environment has neither `dotnet` nor `pwsh`: no local .NET or PowerShell pass is claimed, and these Node tests do not substitute for the new remote correctness matrix. Check the exact new commit's CI and independent review before treating the change as fully validated. This is a CI correctness correction, not a parser speed or allocation improvement.

Verified implementation Git blob fingerprints (the commit's tree is authoritative):

- `.github/workflows/benchmarks.yml`: `21fa6a587276a6ceeec29447d7ad58390a145943`
- `benchmarks/CsvPipeABProbe/ci-status.ts`: `a34d1582b1316ec33c6069895dd00c7d1f90ca11`
- `benchmarks/CsvPipeABProbe/test-ci-status.ts`: `e71c88a5d0d0913ccd068a09bac3d9689ac84ddf`
