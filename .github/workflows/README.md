# GitHub Actions Workflows

This directory contains automated CI/CD workflows for HeroParser.

## Workflows Overview

### 🔨 [ci.yml](./ci.yml) - Build and Test

**Triggers:**
- Every push to any branch
- Pull requests to `main` or `develop`
- Version tags (`v*`)
- Merge queue

**What it does:**
1. **Build and Test** (Matrix: Ubuntu/Windows/macOS × .NET 8/9/10)
   - Restores dependencies
   - Builds in Release configuration
   - Runs all tests
   - Collects code coverage
   - Uploads test results and coverage to Codecov

2. **Code Quality**
   - Verifies no build warnings (TreatWarningsAsErrors=true)
   - Checks code formatting with `dotnet format`

3. **Dependency Review**
   - Scans for vulnerable or incompatible dependencies
   - Blocks GPL-licensed dependencies
   - Comments on PRs with findings

**Artifacts:**
- Test results (30 days retention)
- Code coverage reports (uploaded to Codecov)

---

### 📦 [publish-nuget.yml](./publish-nuget.yml) - Publish to NuGet

**Triggers:**
- When a GitHub Release is published
- Manual workflow dispatch (for republishing)

**What it does:**
1. Extracts version from release tag (e.g., `v1.0.0` → `1.0.0`)
2. Validates version format (semantic versioning)
3. Builds and runs tests
4. Packs NuGet package with symbols
5. Validates package contents
6. Generates package provenance attestation
7. Publishes to NuGet.org
8. Uploads artifacts for record-keeping

**Requirements:**
- GitHub environment: `production`
- NuGet.org Trusted Publishing configured (OIDC - no API keys needed!)

**Usage:**
1. Create a GitHub Release with tag `vX.Y.Z`
2. Workflow automatically publishes to NuGet.org
3. Package available at: https://www.nuget.org/packages/HeroParser

**Manual Republish:**
```bash
# Go to Actions → Publish to NuGet → Run workflow
# Enter version: 1.0.0
```

**Artifacts:**
- NuGet packages (.nupkg and .snupkg) - 90 days retention

---

### 📊 [benchmarks.yml](./benchmarks.yml) - Performance Benchmarks

**Triggers:**
- Pull requests affecting `src/` or `benchmarks/`
- Pushes to `main` affecting `src/` or `benchmarks/`
- Manual workflow dispatch

**What it does:**
1. Runs BenchmarkDotNet throughput tests
2. Tests on .NET 8, 9, and 10
3. Uploads benchmark results as artifacts
4. Posts results as PR comment (for PRs)

**Usage:**
- Benchmarks run automatically on relevant PRs
- Manual run: Actions → Performance Benchmarks → Run workflow

**Artifacts:**
- Benchmark results and reports (30 days retention)

---

## Setup Instructions

### 1. Configure NuGet Trusted Publishing (OIDC)

**No long-lived API keys needed!** NuGet.org supports GitHub OIDC authentication.

1. **Go to NuGet.org Package Registration**
   - Visit: https://www.nuget.org/packages/manage/upload
   - Reserve the package ID: `HeroParser`

2. **Set up Trusted Publishing**
   - Package settings → Trusted publishers
   - Add GitHub Actions as a trusted publisher
   - Repository: `KoalaFacts/HeroParser`
   - Workflow: `publish-nuget.yml`
   - Environment: `production`

3. **Add Required Secrets**

Configure these in **Settings → Secrets and variables → Actions**:

| Secret | Description | How to get |
|--------|-------------|------------|
| `NUGET_USERNAME` | Your NuGet.org username | Your NuGet.org account username |
| `CODECOV_TOKEN` | (Optional) Codecov token | Sign up at https://codecov.io |

**Note**: The `NuGet/login@v1` action uses your username + GitHub OIDC to get a temporary (1-hour) API key. No long-lived API keys stored!

### 2. Required Environments

Create environment in **Settings → Environments**:

- **Name**: `production`
- **Protection rules**:
  - ✅ Required reviewers (recommended)
  - ✅ Wait timer: 5 minutes (optional safety delay)

**Important**: The environment name must be `production` to match the Trusted Publishing configuration on NuGet.org.

### 3. Branch Protection Rules

Recommended settings for `main` branch:

- ✅ Require status checks before merging
  - Required checks:
    - `Build and Test (ubuntu-latest, 10.0.x)`
    - `Build and Test (windows-latest, 10.0.x)`
    - `Build and Test (macos-latest, 10.0.x)`
    - `Code Quality`
- ✅ Require pull request reviews (1 approval)
- ✅ Dismiss stale reviews on new commits
- ✅ Require linear history
- ✅ Include administrators

---

## Publishing a New Version

### Step-by-step Release Process

**All tagging happens in GitHub UI - no local git tags needed!**

1. **Create GitHub Release** (this creates the tag automatically)
   - Go to: https://github.com/KoalaFacts/HeroParser/releases/new
   - Click "Choose a tag" → Type `v1.0.0` → "Create new tag: v1.0.0 on publish"
   - Title: `v1.0.0 - Production Ready Release`
   - Description: Release notes (see template below)
   - Click "Publish release"

2. **Automatic NuGet publishing**
   - `publish-nuget.yml` workflow triggers automatically
   - Extracts version from tag (`v1.0.0` → `1.0.0`)
   - Builds, tests, and publishes to NuGet.org
   - Package available within 5-10 minutes

**No manual git tagging required!** GitHub creates the tag when you publish the release.

### Release Notes Template

```markdown
## What's New in v1.0.0

### 🎉 Production Ready!

HeroParser v1.0.0 is now production-ready with comprehensive security fixes and improvements.

### ✨ Features
- High-performance CSV parsing with SIMD optimizations (AVX-512, AVX2, NEON)
- RFC 4180 quote handling (quoted fields, escaped quotes, delimiters in quotes)
- Zero-allocation design with lazy column evaluation
- Multi-framework support (.NET 8, 9, 10)

### 🔒 Security Fixes
- Added bounds checking for column indexers
- Integer overflow protection in SIMD processing
- Resource management documentation

### 📦 Installation

```bash
dotnet add package HeroParser --version 1.0.0
```

### 📝 Full Changelog
See [CHANGELOG.md](CHANGELOG.md) for complete details.
```

---

## Workflow Status Badges

Add to your README.md:

```markdown
[![Build and Test](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml/badge.svg)](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/HeroParser.svg)](https://www.nuget.org/packages/HeroParser)
[![codecov](https://codecov.io/gh/KoalaFacts/HeroParser/branch/main/graph/badge.svg)](https://codecov.io/gh/KoalaFacts/HeroParser)
```

---

## Troubleshooting

### CI Workflow Issues

**Build fails on specific OS:**
- Check matrix strategy in `ci.yml`
- Review OS-specific dependencies
- Examine test results artifact

**Tests fail intermittently:**
- Check for race conditions
- Review timeout settings
- Enable verbose logging: `--verbosity detailed`

### NuGet Publishing Issues

**Authentication failed with Trusted Publishing:**
- Verify Trusted Publishing is configured on NuGet.org
- Check repository name matches: `KoalaFacts/HeroParser`
- Verify workflow name matches: `publish-nuget.yml`
- Ensure environment name is: `production`
- Confirm package ID is reserved on NuGet.org

**Package version conflict:**
- NuGet.org doesn't allow republishing same version
- Increment version number
- Use `--skip-duplicate` flag (already configured)

**Missing symbols package:**
- Verify `SymbolPackageFormat=snupkg` in pack command
- Check build artifacts for `.snupkg` file

### Benchmark Issues

**Benchmarks timeout:**
- Increase `timeout-minutes` in workflow
- Reduce benchmark iterations
- Check for infinite loops

**Results not posted to PR:**
- Verify PR has write permissions
- Check benchmark output format
- Review GitHub Actions logs

---

## Maintenance

### Regular Tasks

**Monthly:**
- Review and update GitHub Actions versions (`@v5` → `@v6` etc.)
- Check for deprecated features
- Update .NET SDK versions as new releases come out

**Quarterly:**
- Audit dependencies for vulnerabilities
- Review and update branch protection rules
- Clean up old workflow artifacts

### Updating Workflows

When modifying workflows:

1. **Test on feature branch first**
   ```bash
   git checkout -b test-workflow-changes
   # Edit .github/workflows/*.yml
   git push origin test-workflow-changes
   # Create PR and verify workflows run correctly
   ```

2. **Use workflow dispatch for testing**
   - Manual triggers allow testing without commits
   - Useful for debugging complex workflow logic

3. **Validate YAML syntax**
   ```bash
   # Use yamllint or online validators
   yamllint .github/workflows/*.yml
   ```

---

## Additional Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [BenchmarkDotNet Guide](https://benchmarkdotnet.org/articles/guides/index.html)
- [NuGet Publishing Guide](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package)
- [Codecov Documentation](https://docs.codecov.com/)

---

**Questions or Issues?**

Open an issue at: https://github.com/KoalaFacts/HeroParser/issues
