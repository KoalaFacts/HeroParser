# Archived: historical CSV Pipe timing experiment

## Disposition

On 2026-10-25 the maintainer accepted retiring this experiment rather than keeping it as a permanent gate or development task. The experiment is **closed and archived**, not awaiting another attempt to become green. This supersedes the live-history reporting and manual timing entry described in [the earlier CI separation report](2026-10-25-csv-pipe-ci-separation.md).

The retirement removes the `pipe-ab` job and the `pipe_ab` / `pipe_controls` dispatch inputs from the ordinary benchmark workflow. Current correctness reports no longer import or display the historical verdict, thresholds or remaining budget. Status JSON schema version 2 drops `historical`; the current untimed measurement metadata still explicitly prohibits a speedup claim. Routine summaries show current correctness and policy outcomes, not a perpetual historical failure or unmeasured-timing warning.

Current .NET 8/9/10 MTP correctness tests, minimum test count, fail-skips, TRX validation, synthetic control validators and ordinary performance benchmarks remain enabled. Missing, failed, cancelled or skipped prerequisites still fail the current check. Independent manual diagnostic jobs and their scripts are not removed or run by this patch; the separate diagnostic work in PR #147 is not modified. No branch protection, approval, release workflow or production parser changes are included.

## Preserved evidence

The original result is not rewritten: [run 36732093817](https://github.com/KoalaFacts/HeroParser/actions/runs/36732093817), source `89c06810e76c4623ebad3cfc89c4bcdef41acd59`, same-source Segmented128 ratio **1.052845684**, original maximum **1.05**, exhausted sampling budget, original acceptance **FAILED**. That describes the archived experiment, not the status of every future parser version.

Existing samples, hashes and analysis remain in [the process-variance archive](csv-pipe-process-variance/README.md) and [benchmark-validity report](2026-10-24-benchmark-validity.md). Diagnostic and sampling scripts remain at their existing paths for provenance; retiring the workflow is not permission to rerun the exhausted experiment. There is no new sampling or reinterpretation of old samples in this change.

Future performance work must address a current, explicit hypothesis with equivalent inputs and valid measurements. It does not inherit an obligation to make this archived experiment pass. Routine project monitoring must not list its verdict as an open blocker or repeatedly notify the maintainer about it.

## Verification

Parent source: `1256fbe3eee5c6832c01f43cd92a24050187faa1` (PR #157).

The updated native TypeScript regression suite, run with Node v22.16.0 using `node --experimental-strip-types --test benchmarks/CsvPipeABProbe/test-ci-status.ts`, first reproduced the missing retirement: **24 tests, 14 passed, 10 failed, none skipped**. After implementation: **24 passed, none failed or skipped**. This includes executing the actual workflow reporter with successful and failed prerequisites, plus normal event routing and independent diagnostic isolation.

YAML structural comparison confirms that the ordinary benchmark job and every current correctness command are unchanged; unrelated manual diagnostics only lose references to the two removed inputs. The existing no-cancellation concurrency policy is retained. No historical scripts or raw samples are changed. Local .NET and PowerShell are unavailable, so their remote checks on the exact new commit remain necessary; these Node results do not substitute for them. No performance improvement is claimed.
