import fs from "fs";
import path from "path";

const rootDir = path.resolve(import.meta.dirname, "../..");

function readOptional(path) {
  try {
    return fs.readFileSync(path, "utf8");
  } catch (error) {
    if (error.code === "ENOENT") return null;
    throw error;
  }
}

// 1. Read version from Directory.Build.props
const propsPath = path.join(rootDir, "Directory.Build.props");
const propsContent = fs.readFileSync(propsPath, "utf8");
const versionMatch = propsContent.match(/<Version>(.*)<\/Version>/);
if (!versionMatch) {
  console.error("Could not find <Version> tag in Directory.Build.props!");
  process.exit(1);
}
const version = versionMatch[1].trim();
console.log(`Synchronizing version: ${version}`);

// 2. Update npm/packages/heroparser/package.json
const pkgPath = path.join(rootDir, "npm/packages/heroparser/package.json");
const pkg = JSON.parse(fs.readFileSync(pkgPath, "utf8"));
if (pkg.version !== version) {
  pkg.version = version;
  fs.writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + "\n");
  console.log(`Updated package.json version to ${version}`);
}

// 2a. Keep npm's workspace lock entry aligned with the package manifest.
// npm ci reads this metadata; update both whenever the central version changes.
const lockPath = path.join(rootDir, "npm/package-lock.json");
const lockText = fs.readFileSync(lockPath, "utf8");
const lockEntry = JSON.parse(lockText).packages?.["packages/heroparser"];
const workspaceVersion = /("packages\/heroparser"\s*:\s*\{\s*"version"\s*:\s*")[^"]+(")/g;
if (!lockEntry || [...lockText.matchAll(workspaceVersion)].length !== 1) {
  throw new Error("Cannot uniquely locate packages/heroparser version in npm/package-lock.json");
}
if (lockEntry.version !== version) {
  fs.writeFileSync(
    lockPath,
    lockText.replace(workspaceVersion, (_match, before, after) => before + version + after),
  );
  console.log("Updated npm/package-lock.json workspace version to " + version);
}

// 3. Update snap/snapcraft.yaml
const snapPath = path.join(rootDir, "snap/snapcraft.yaml");
let snapContent = readOptional(snapPath);
if (snapContent !== null) {
  snapContent = snapContent.replace(/version:\s*['"]?.*['"]?/, `version: '${version}'`);
  fs.writeFileSync(snapPath, snapContent);
  console.log(`Updated snapcraft.yaml version to ${version}`);
}

// 4. Update install.sh
const installPath = path.join(rootDir, "install.sh");
let installContent = readOptional(installPath);
if (installContent !== null) {
  installContent = installContent.replace(/DEFAULT_VERSION=".*"/, `DEFAULT_VERSION="${version}"`);
  fs.writeFileSync(installPath, installContent);
  console.log(`Updated install.sh version to ${version}`);
}

// 5. Update README.md CLI version references
const readmePath = path.join(rootDir, "README.md");
let readmeContent = readOptional(readmePath);
if (readmeContent !== null) {
  readmeContent = readmeContent.replace(/--version\s+\d+\.\d+\.\d+/g, `--version ${version}`);
  fs.writeFileSync(readmePath, readmeContent);
  console.log(`Updated README.md version references to ${version}`);
}

// 6. Update docs/cli.md version references
const docsCliPath = path.join(rootDir, "docs/cli.md");
let docsCliContent = readOptional(docsCliPath);
if (docsCliContent !== null) {
  docsCliContent = docsCliContent.replace(/--version\s+\d+\.\d+\.\d+/g, `--version ${version}`);
  fs.writeFileSync(docsCliPath, docsCliContent);
  console.log(`Updated docs/cli.md version references to ${version}`);
}

// 7. Update snap/README.md snap package version references
const snapReadmePath = path.join(rootDir, "snap/README.md");
let snapReadmeContent = readOptional(snapReadmePath);
if (snapReadmeContent !== null) {
  snapReadmeContent = snapReadmeContent.replace(
    /heroparser_\d+\.\d+\.\d+_amd64\.snap/g,
    `heroparser_${version}_amd64.snap`,
  );
  fs.writeFileSync(snapReadmePath, snapReadmeContent);
  console.log(`Updated snap/README.md snap package references to ${version}`);
}

// 8. Update CHANGELOG.md (auto-insert release header under Unreleased if missing)
const changelogPath = path.join(rootDir, "CHANGELOG.md");
let changelogContent = readOptional(changelogPath);
if (changelogContent !== null) {
  const versionHeader = `## [${version}]`;
  if (!changelogContent.includes(versionHeader)) {
    const today = new Date().toISOString().split("T")[0];
    const newHeader = `## [Unreleased]\n\n## [${version}] - ${today}`;
    changelogContent = changelogContent.replace(/##\s*\[Unreleased\]/i, newHeader);
    fs.writeFileSync(changelogPath, changelogContent);
    console.log(`Updated CHANGELOG.md: Added release section for v${version}`);
  }
}

// 9. Update src/HeroParser/HeroParser.csproj PackageReleaseNotes
const csprojPath = path.join(rootDir, "src/HeroParser/HeroParser.csproj");
let csprojContent = readOptional(csprojPath);
if (csprojContent !== null) {
  csprojContent = csprojContent.replace(
    /<PackageReleaseNotes>v\d+\.\d+\.\d+/,
    `<PackageReleaseNotes>v${version}`,
  );
  fs.writeFileSync(csprojPath, csprojContent);
  console.log(`Updated HeroParser.csproj PackageReleaseNotes to v${version}`);
}
