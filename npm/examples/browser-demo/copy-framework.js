import fs from 'fs';
import path from 'path';

const srcDir = path.resolve('../../packages/heroparser-wasm/_framework');
const destDir = path.resolve('dist/_framework');

function copyDir(src, dest) {
    fs.mkdirSync(dest, { recursive: true });
    const entries = fs.readdirSync(src, { withFileTypes: true });
    for (let entry of entries) {
        const srcPath = path.join(src, entry.name);
        const destPath = path.join(dest, entry.name);
        if (entry.isDirectory()) {
            copyDir(srcPath, destPath);
        } else {
            fs.copyFileSync(srcPath, destPath);
        }
    }
}

try {
    console.log(`Copying WASM framework from ${srcDir} to ${destDir}...`);
    copyDir(srcDir, destDir);
    console.log("WASM framework copied successfully!");
} catch (err) {
    console.error("Failed to copy framework:", err);
    process.exit(1);
}
