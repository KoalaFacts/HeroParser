# CSV Pipe column decoding: validated text and meaningful segment boundaries

PR #157, 2026-10-25. Test-first commit: `9696a56db4cbada74f8fda7de7a7296b195155f5`. Changes are confined to benchmarks, xUnit integration tests, and documentation; the production parser is unchanged.

## Defect in the earlier measurement setup

`CsvPipeColumnDecodingBenchmarks` originally divided each borrowed field at `bytes.Length / 2`. That did not reliably force a doubled quote, backslash-quote pair, or multibyte UTF-8 character across segment boundaries. Neither timed method was checked against independently expected decoded text before BenchmarkDotNet measurement. A partial decode or wrong replacement policy could therefore be timed and reported without failing.

The updated GlobalSetup requires exact decoded text from both actual timed methods (`OwnedColumn` and `SegmentedColumn`). It deliberately splits the two-segment borrowed sequence at the relevant token or UTF-8 byte sequence and rejects invalid segment indices:

| Scenario | Forced split | Exact decoded output |
| --- | --- | --- |
| Plain | Halfway through plain bytes | `an ordinary field without CSV escapes` |
| DoubledQuotes | Between the first interior `""` | `she said "hello" to the reader` |
| Escaped | Between the escape backslash and escaped quote | `she said "hello" to the reader` |
| Unicode | Within the UTF-8 code point for the first Chinese character | `你好 "世界" 😀` |
| LongEscaped | Between doubled quotes near the midpoint of 512 repetitions | `value" ` repeated 512 times |
| InvalidUtf8 | Immediately after the invalid C3 lead byte | U+FFFD, U+FFFD, `x` |

`InvalidUtf8` uses raw bytes `22 C3 5C A9 78 22`: the invalid UTF-8 lead byte `C3` is separated from continuation byte `A9` by the CSV escape character `5C`. Correct behavior decodes UTF-8 first, producing two U+FFFD replacements, and then removes the escape character. Unescaping the raw bytes first could instead transform `C3 A9` into the valid character U+00E9, which would be semantically incorrect. This independently stated expected output is asserted for the original owned and forced-segmented decoding paths.

New integration tests invoke the actual benchmark entry points for all six scenarios; `InvalidUtf8` is deliberately absent from the old benchmark and would fail before this repair. Full output equality matters: a count-only or byte-length-only check would not protect decoding semantics.

## Evidence and limits

This is a benchmark-validity improvement only, not a demonstrated speedup or reduction in allocations. These new segment layouts and scenario coverage cannot be compared directly with the earlier September baseline. Any new timing claim requires complete-field preflight, identical source/assembly and hardware/runtime provenance, valid same-runner A/A controls, a fixed paired A/B budget, and raw B/op and GC samples.

The local runner lacks a dotnet SDK and PowerShell. No local xUnit red/green run or .NET performance test is claimed; the exact new HEAD requires its automatically triggered remote .NET 8/9/10 CI and an independent review.

The frozen historical CSV Pipe A/A control result remains FAILED at run 36732093817 (Segmented128 1.052845684 > 1.05, remaining sampling budget zero). Neither its samples nor its decision are changed or retried.
