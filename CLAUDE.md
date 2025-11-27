# HeroParser Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-09-22

## Active Technologies
- C# with multi-framework targeting (netstandard2.0, netstandard2.1, net6.0, net7.0, net8.0, net9.0, net10.0) + BenchmarkDotNet (performance validation), Source Generators (allocation-free mapping), Zero external dependencies for core library (001-aim-to-be)

## Project Structure
```
src/
tests/
```

## Commands
# Add commands for C# with multi-framework targeting (netstandard2.0, netstandard2.1, net6.0, net7.0, net8.0, net9.0, net10.0)

## Code Style
C# with multi-framework targeting (netstandard2.0, netstandard2.1, net6.0, net7.0, net8.0, net9.0, net10.0): Follow standard conventions

## Recent Changes
- 001-aim-to-be: Added C# with multi-framework targeting (netstandard2.0, netstandard2.1, net6.0, net7.0, net8.0, net9.0, net10.0) + BenchmarkDotNet (performance validation), Source Generators (allocation-free mapping), Zero external dependencies for core library

<!-- MANUAL ADDITIONS START -->

## Code Quality Rules

### Never Suppress Warnings to Bypass Issues
- **NEVER use `#pragma warning disable` to suppress warnings that indicate real issues**
- Fix the underlying problem instead of hiding it
- Acceptable suppressions are only for:
  - False positives with clear justification comments
  - Intentional API design decisions (e.g., `IDE0060` for API compatibility where parameter is intentionally unused)
  - Framework limitations that cannot be resolved (e.g., `IsExternalInit` for older frameworks)
- For xUnit tests: Use `TestContext.Current.CancellationToken` instead of suppressing `xUnit1051`
- Always include a justification comment explaining WHY the suppression is acceptable

<!-- MANUAL ADDITIONS END -->