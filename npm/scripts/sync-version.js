import fs from 'fs';
import path from 'path';

const rootDir = path.resolve(import.meta.dirname, '../..');

// 1. Read version from Directory.Build.props
const propsPath = path.join(rootDir, 'Directory.Build.props');
const propsContent = fs.readFileSync(propsPath, 'utf8');
const versionMatch = propsContent.match(/<Version>(.*)<\/Version>/);
if (!versionMatch) {
    console.error("Could not find <Version> tag in Directory.Build.props!");
    process.exit(1);
}
const version = versionMatch[1].trim();
console.log(`Synchronizing version: ${version}`);

// 2. Update npm/packages/heroparser/package.json
const pkgPath = path.join(rootDir, 'npm/packages/heroparser/package.json');
const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf8'));
if (pkg.version !== version) {
    pkg.version = version;
    fs.writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + '\n');
    console.log(`Updated package.json version to ${version}`);
}

// 3. Update snap/snapcraft.yaml
const snapPath = path.join(rootDir, 'snap/snapcraft.yaml');
if (fs.existsSync(snapPath)) {
    let snapContent = fs.readFileSync(snapPath, 'utf8');
    snapContent = snapContent.replace(/version:\s*['"]?.*['"]?/, `version: '${version}'`);
    fs.writeFileSync(snapPath, snapContent);
    console.log(`Updated snapcraft.yaml version to ${version}`);
}

// 4. Update install.sh
const installPath = path.join(rootDir, 'install.sh');
if (fs.existsSync(installPath)) {
    let installContent = fs.readFileSync(installPath, 'utf8');
    installContent = installContent.replace(/DEFAULT_VERSION=".*"/, `DEFAULT_VERSION="${version}"`);
    fs.writeFileSync(installPath, installContent);
    console.log(`Updated install.sh version to ${version}`);
}

// 5. Update README.md CLI version references
const readmePath = path.join(rootDir, 'README.md');
if (fs.existsSync(readmePath)) {
    let readmeContent = fs.readFileSync(readmePath, 'utf8');
    readmeContent = readmeContent.replace(/--version\s+\d+\.\d+\.\d+/g, `--version ${version}`);
    fs.writeFileSync(readmePath, readmeContent);
    console.log(`Updated README.md version references to ${version}`);
}

// 6. Update docs/cli.md version references
const docsCliPath = path.join(rootDir, 'docs/cli.md');
if (fs.existsSync(docsCliPath)) {
    let docsCliContent = fs.readFileSync(docsCliPath, 'utf8');
    docsCliContent = docsCliContent.replace(/--version\s+\d+\.\d+\.\d+/g, `--version ${version}`);
    fs.writeFileSync(docsCliPath, docsCliContent);
    console.log(`Updated docs/cli.md version references to ${version}`);
}

// 7. Update snap/README.md snap package version references
const snapReadmePath = path.join(rootDir, 'snap/README.md');
if (fs.existsSync(snapReadmePath)) {
    let snapReadmeContent = fs.readFileSync(snapReadmePath, 'utf8');
    snapReadmeContent = snapReadmeContent.replace(/heroparser_\d+\.\d+\.\d+_amd64\.snap/g, `heroparser_${version}_amd64.snap`);
    fs.writeFileSync(snapReadmePath, snapReadmeContent);
    console.log(`Updated snap/README.md snap package references to ${version}`);
}

// 8. Update CHANGELOG.md (auto-insert release header under Unreleased if missing)
const changelogPath = path.join(rootDir, 'CHANGELOG.md');
if (fs.existsSync(changelogPath)) {
    let changelogContent = fs.readFileSync(changelogPath, 'utf8');
    const versionHeader = `## [${version}]`;
    if (!changelogContent.includes(versionHeader)) {
        const today = new Date().toISOString().split('T')[0];
        const newHeader = `## [Unreleased]\n\n## [${version}] - ${today}`;
        changelogContent = changelogContent.replace(/##\s*\[Unreleased\]/i, newHeader);
        fs.writeFileSync(changelogPath, changelogContent);
        console.log(`Updated CHANGELOG.md: Added release section for v${version}`);
    }
}
