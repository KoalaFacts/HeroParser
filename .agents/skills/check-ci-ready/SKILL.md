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
dotnet test --project tests/HeroParser.Tests -c Release --no-build -- --filter-trait Category=Unit
```

4. **Integration tests**:
```bash
dotnet test --project tests/HeroParser.Tests -c Release --no-build -- --filter-trait Category=Integration
```

5. **Source generator tests**:
```bash
dotnet test --project tests/HeroParser.Generators.Tests -c Release --no-build
```

6. **AOT compatibility tests**:
```bash
dotnet run --project tests/HeroParser.AotTests -c Release
```

7. **Docs check** (Node 24+):
```bash
node --test .github/scripts/tests/check-docs.test.ts
node .github/scripts/check-docs.ts
```

8. Report a summary: which steps passed, which failed, and any warnings.
