# TRX evidence fixtures

- `empty.trx` is the actual unit report from PR #149, workflow run `39211079662`,
  artifact `12167597481` (Ubuntu / .NET 10). The command reported 4,209 passing
  unit tests, but the TRX contains empty results and all-zero counters.
  Source: https://github.com/KoalaFacts/HeroParser/actions/runs/39211079662/job/117925156487
- `passed.trx` is a minimal synthetic report using the same TRX namespace and
  counter layout. Tests derive malformed, partial, failed and skipped variants
  from it without hardcoding HeroParser's current test count.

Run the dependency-free regression suite from the repository root:

```powershell
pwsh -NoProfile -File .github/scripts/tests/test-validate-test-results.ps1
```

The validator checks report integrity, not test success. Consistent failed-test
reports remain publishable for diagnostics; the original `dotnet test` exit
status still fails CI. Both expected report paths are required independently.
The raw reports are uploaded even if validation fails.

The candidate producer fix is tracked separately in PR #140 (TrxReport 2.5.0 to 2.5.1).
Its historical artifact `11769201774` contains 4,209 unit and 324 integration
records and passes this guard. This fixture check does not replace fresh build,
test, review, or CI validation of the final integrated revision.

## Outcome contract

`NotExecuted` and `NotRunnable` are unrun records and do not contribute to
`executed`. Consistent mixed reports can include these records, but an entirely
unrun suite is not execution evidence. Terminal outcomes (`Passed`, `Failed`,
`Error`, `Timeout`, `Aborted`, `Inconclusive`, `PassedButRunAborted`, `Completed`)
remain publishable when their counters match; accepting a report never converts
failed tests into successful tests.

`Pending` and `InProgress` are nonterminal. `Warning` is a run-level status, not
a per-test outcome. The guard rejects these even when mixed with passing tests.
It also rejects `Disconnected` as incomplete evidence: this is a conservative
completed-report policy, not a claim that TRX syntax prohibits the status.
Raw reports remain uploaded for diagnosis.

The classifications follow Microsoft's
[TRX outcome definitions](https://github.com/microsoft/vstest/blob/main/src/Microsoft.TestPlatform.Extensions.TrxLogger/ObjectModel/TestOutcome.cs).
The current
[MTP TRX producer](https://github.com/microsoft/testfx/blob/main/src/Platform/Microsoft.Testing.Extensions.TrxReport/TrxReportEngine.Results.cs)
emits `Passed`, `Failed`, and `NotExecuted` for ordinary test results.
