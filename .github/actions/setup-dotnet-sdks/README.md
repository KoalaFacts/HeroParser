# Setup .NET SDKs Action

A reusable composite action that automatically installs all .NET SDK versions defined in `Directory.Build.props`.

## Usage

```yaml
- name: Setup .NET SDKs
  uses: ./.github/actions/setup-dotnet-sdks
```

With caching disabled:

```yaml
- name: Setup .NET SDKs
  uses: ./.github/actions/setup-dotnet-sdks
  with:
    cache: false
```

## How It Works

1. Extracts `<SupportedSdkVersions>` from the repository's `Directory.Build.props`
2. Installs all SDK versions using `actions/setup-dotnet@v5`
3. Enables NuGet package caching by default

## Benefits

- **Single source of truth**: SDK versions defined once in `Directory.Build.props`
- **No duplication**: All workflows use the same action
- **Easy maintenance**: Update SDK versions in one place
- **Consistent**: All workflows use identical SDK setup

## Outputs

- `versions`: The extracted SDK versions (newline-separated string)
- `frameworks`: The extracted target frameworks as JSON array (e.g., `["net8.0","net9.0","net10.0"]`)

## Requirements

- Repository must have a `Directory.Build.props` file at the root
- The file must contain a `<SupportedSdkVersions>` element with semicolon-separated versions
- Example: `<SupportedSdkVersions>8.0.x;9.0.x;10.0.x</SupportedSdkVersions>`
