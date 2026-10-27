# Write-side CSV injection benchmark validity — 2026-10-25

## Defect and independent workload

Parent revision 922df0d96379f343d1183c96dcf6f3c536a74f88 on PR #157 has WriteInjectionProtectionBenchmarks, which exercises only safe strings, signed numbers, and phone numbers. Its method names imply that Sanitize/Reject/EscapeWithQuote costs include formula protection, but the writer never encounters an actually dangerous prefix in those timed cases. That benchmark is still valid **only** as the cost of enabling the policy on harmless input.

A new class, WriteInjectionDangerousBenchmarks, uses an identical four-field row for all measured policies:

- Logical fields: =cmd, -100, hello,world, +42
- None output: =cmd,-100,"hello,world",+42
- Sanitize output: cmd,-100,"hello,world",+42
- EscapeWithQuote output: "'=cmd",-100,"hello,world",+42
- EscapeWithTab output: "<TAB>=cmd",-100,"hello,world",+42 (literal tab inside the quotes)

Each CSV output row terminates with the configured CRLF. The real 10,000 and 100,000 row timed methods return fully materialized output strings; GlobalSetup invokes every actual timed method and compares **every row's full output** to an independently written expected row, checking exact total length and row boundaries before recording timing. Signed numbers must remain unchanged, and the comma-containing field must be quoted. The Reject policy is validated on the same input by requiring CsvErrorCode.InjectionDetected, but is **not timed** because throwing per row is not a successful-write workload.

The xUnit v3/Microsoft.Testing.Platform integration regression invokes all four timed methods at 1, 9, and 1,000 rows and compares entire output strings, including CRLF, against independent expected values. The original head contains no positive-input timed writing fixture or independent output validation. The separate new class uses its own None baseline, so it does not compare different source input with the existing harmless-input class.

## Measurement, security and release limits

This change adds only benchmark/test/docs and benchmark registration. It does not alter CsvStreamWriter, the security policy implementation, or any shipped public API. It neither claims new security behavior nor throughput/allocation improvements. None, Sanitize, and Escape* produce **intentionally different** output bytes, so their relative cost is conditional on those output contracts, not identical byte-for-byte serialization. Do not compare preexisting safe-input results against new dangerous-input results as a same-workload speedup.

No old CsvPipeABProbe sampling was retried or authorized. Historical run 36732093817 remains FAILED (Segmented128 ratio 1.052845684 > 1.05, remaining budget zero). Test-first Git commit preceded implementation but was not run locally because this execution container lacks dotnet/pwsh; exact-head CI is required before declaring .NET 8/9/10 success. This is benchmark coverage, not a parser optimization.
