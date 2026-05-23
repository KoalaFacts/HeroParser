---
name: check-ci-ready
description: Run full CI-equivalent checks locally before pushing
disable-model-invocation: true
---

# Check CI Readiness

Run the same checks that CI enforces, in order. Stop on first failure.

## Steps

1. **Format check**:
```bash
dotnet format --verify-no-changes
```
If this fails, run `dotnet format` to fix, then report what changed.

2. **Build** (Release, like CI):
```bash
dotnet build -c Release
```

3. **Unit tests**:
```bash
dotnet test -c Release --no-build --filter Category=Unit
```

4. **Integration tests**:
```bash
dotnet test -c Release --no-build --filter Category=Integration
```

5. **Source generator tests**:
```bash
dotnet test tests/HeroParser.Generators.Tests -c Release --no-build
```

6. **AOT compatibility tests**:
```bash
dotnet run --project tests/HeroParser.AotTests -c Release
```

7. Report a summary: which steps passed, which failed, and any warnings.
