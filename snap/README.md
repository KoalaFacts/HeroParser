# Snap Packaging for HeroParser CLI

This directory contains the packaging configuration required to build and publish the `heroparser` command-line tool as a Snap package.

## Structure
- [snapcraft.yaml](snapcraft.yaml): The main configuration file defining the application, base environment, dependencies, permissions, and build steps.

## How to Build

To build the snap package locally, you will need `snapcraft` and a supported virtualization/container hypervisor (such as Multipass or LXD):

1. **Build the CLI binary**:
   Ensure you compile the `HeroParser.Cli` release binary first so that it is available in the expected output directory (e.g., `./bin/`):
   ```bash
   dotnet publish src/HeroParser.Cli/HeroParser.Cli.csproj -c Release -r linux-x64 --self-contained -o bin/
   ```

2. **Build the Snap**:
   Run `snapcraft` from the root directory of the repository:
   ```bash
   snapcraft
   ```
   This will produce a `.snap` package (e.g., `heroparser_2.5.2_amd64.snap`).

## Testing the Snap Locally

To install and test the generated snap package locally in strict confinement:
```bash
sudo snap install heroparser_2.5.2_amd64.snap --dangerous
```

## Publishing to the Snap Store

Once tested, the package can be registered and pushed to the Snap Store:

1. **Log in**:
   ```bash
   snapcraft login
   ```

2. **Upload & Publish**:
   ```bash
   snapcraft upload --release=stable heroparser_2.5.2_amd64.snap
   ```
