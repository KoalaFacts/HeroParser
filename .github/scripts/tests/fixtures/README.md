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
