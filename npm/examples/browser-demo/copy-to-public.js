import fs from 'fs';
import path from 'path';
import { createRequire } from 'module';

const srcDir = path.resolve('../../packages/heroparser/_framework');
const destDir = path.resolve('public/_framework');

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
    console.log("WASM framework copied to public successfully!");

    // Copy onnxruntime-web WASM and JS files to public/assets
    const require = createRequire(import.meta.url);
    const ortMainFile = require.resolve('onnxruntime-web');
    const ortSrcDir = ortMainFile.includes('dist') 
        ? path.dirname(ortMainFile) 
        : path.join(path.dirname(ortMainFile), 'dist');
    const destRoot = path.resolve('public');
    const destAssets = path.resolve('public/assets');

    fs.mkdirSync(destAssets, { recursive: true });

    const ortFiles = [
        { src: 'ort-wasm-simd-threaded.mjs', dest: 'ort-wasm-simd-threaded.asyncify.mjs' },
        { src: 'ort-wasm-simd-threaded.wasm', dest: 'ort-wasm-simd-threaded.asyncify.wasm' },
        { src: 'ort-wasm-simd-threaded.mjs', dest: 'ort-wasm-simd-threaded.mjs' },
        { src: 'ort-wasm-simd-threaded.wasm', dest: 'ort-wasm-simd-threaded.wasm' },
        { src: 'ort-wasm-simd-threaded.jsep.mjs', dest: 'ort-wasm-simd-threaded.jsep.mjs' },
        { src: 'ort-wasm-simd-threaded.jsep.wasm', dest: 'ort-wasm-simd-threaded.jsep.wasm' }
    ];

    console.log("Copying ONNX Runtime Web WASM assets to public/assets...");
    for (const file of ortFiles) {
        const srcPath = path.join(ortSrcDir, file.src);
        if (fs.existsSync(srcPath)) {
            // Copy to public/
            fs.copyFileSync(srcPath, path.join(destRoot, file.dest));
            // Copy to public/assets/
            fs.copyFileSync(srcPath, path.join(destAssets, file.dest));
            console.log(`Copied ${file.src} -> ${file.dest}`);
        } else {
            console.warn(`Warning: Source file not found: ${srcPath}`);
        }
    }
    console.log("ONNX Runtime Web WASM assets copied to public successfully!");
} catch (err) {
    console.error("Failed to copy framework or WASM assets to public:", err);
    process.exit(1);
}
