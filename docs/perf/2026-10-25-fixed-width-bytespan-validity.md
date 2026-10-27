# Fixed-width char and UTF-8 byte-span benchmark validity — 2026-10-25

## Distinct defect

`FixedWidthByteSpanBenchmarks` in `FixedWidthWriterBenchmarks.cs` generates **1-based IDs** and `Value = 100 * ID` for up to 100,000 fixed 40-character records (10 ID + 20 Name + 10 Value). Previously the three actual timed methods accumulated in `int`. At 100,000 rows the correct ID-only sum is **5,000,050,000**, above `Int32.MaxValue`; the byte-span variant that also parses Value and counts Name field length overflows already at 10,000 rows. Despite every individual value being valid, the reported 32-bit checksums wrapped silently.

## Correctness contract

The test-first xUnit integration now invokes all **three real timed methods** at 1,000, 10,000 and 100,000 rows. It requires `long` results and independently computes:

- `ID = Rows * (Rows + 1) / 2`
- `NameLengths = Σ(4 + decimal digit count(i))` for `i=1..Rows`, after trimming the right-padding spaces
- `AllFields = 101 * ID + NameLengths` (100 * ID accounts for Value)

At 100,000 rows the exact expected ID checksum is **5,000,050,000** and the expected mixed checksum is **505,005,938,895** (name-length sum **888,895**). At 10,000 rows the all-fields result is **5,050,583,894**. Old `int` results fail both the long-type and value assertions.

`GlobalSetup` now independently verifies *every row* in the char and UTF-8 representations against its intended record number, exact 40-unit width, ID, decoded Name, and Value. It checks both row counts and the actual timed methods' full 64-bit results before measuring. Parsing and its options remain unchanged; only accumulator and return-value widths and setup validation changed.

**Workload classification:** `ParseFromCharSpan` and `ParseFromUtf8ByteSpan` parse the ID primitive only. `ParseFromUtf8ByteSpan_AllFields` parses ID and Value but only counts the decoded/truncated Name span's length; it does **not** decode the Name into an application string or materialize a typed record. It must not be reported as full business deserialization.

## Evidence, comparisons, and limits

This change touches **benchmark, integration test, and documentation only**. It does not modify the production parser or claim throughput/allocation improvements. The input shape stays 10+20+10 ASCII fields and the same 1k/10k/100k sizes, but 64-bit return/checksum changes can alter measured cost; therefore use a new baseline for comparisons and retain CPU, runtime, source/assembly hashes and raw samples. The historical frozen CSV Pipe FAILED experiment is unrelated and untouched.

The test-first commit intentionally fails against the original timed return types. No `dotnet` SDK or PowerShell is available in this execution container, so no local C# compile/test result or quantitative speedup is claimed. The exact final PR HEAD must pass GitHub Actions .NET8/9/10 builds, strict analyzers, full xUnit suites and benchmark setup before this slice is accepted.
