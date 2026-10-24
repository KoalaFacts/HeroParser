# CSV column parsing benchmark: lossless unsigned checksum

## Problem and scope

Audited source parent: `5dc55df004a81f8a93586b6ce277840d0309dc2d`. The `ColumnParseBenchmarks` fixture contains the valid unsigned value **4294967295**. Both the char and UTF-8 timed methods previously cast it to `int`, turning the value into **-1**. Their `int` checksum was therefore **128** rather than the lossless total **4,294,967,424**. The fixture was also not prevalidated before timing, so a failed conversion could silently supply a zero contribution.

## Correction

Both timed methods accumulate and return `long`, preserving the `uint` value. `GlobalSetup` validates every field in both char and UTF-8 readers: six columns, `Int32=123`, `UInt32=4294967295`, a date with seconds `56`, the Berlin time zone base UTC offset `+01:00`, boolean `true`, floating value `3.14`, and exactly one logical row. It then checks the two actual timed results **4,294,967,424** and the original culture/format method result **35** before measurement. An xUnit v3/Microsoft.Testing.Platform integration regression invokes all three actual timed methods, asserts the `long` type for the two full checksums, and checks their independently calculated expected values.

This is a benchmark validity/return-type correction only: **no production parser, workload input, quote/UTF-8 semantics, historical CSV Pipe evidence, or frozen timing gate is changed.** The measured return contract and setup have changed, so compare new results only with a separately validated equivalent baseline. This is not an acceleration or allocation claim.

## Evidence and limitations

The test-first commit was constructed before the fix to require lossless `long` output (the previous `int` implementation fails this assertion). This environment does not have a `.NET` SDK, so the test-first red phase and revised C# test have **not** been executed locally. Use the exact new HEAD's GitHub .NET 8/9/10 CI results, build/analyzer checks and independent review for validation; a prior SHA's green results do not count. No local timing, A/A or A/B was run. Historical CsvPipeABProbe FAILED status and zero remaining sampling budget are unchanged.
