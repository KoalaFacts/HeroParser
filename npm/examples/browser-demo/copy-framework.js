import fs from "fs";
import path from "path";
import { createRequire } from "module";

const srcDir = path.resolve("../../packages/heroparser/_framework");
const destDir = path.resolve("dist/_framework");

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

  // Copy onnxruntime-web WASM and JS files
  const require = createRequire(import.meta.url);
  const ortMainFile = require.resolve("onnxruntime-web");
  const ortSrcDir = ortMainFile.includes("dist")
    ? path.dirname(ortMainFile)
    : path.join(path.dirname(ortMainFile), "dist");
  const destRoot = path.resolve("dist");
  const destAssets = path.resolve("dist/assets");

  fs.mkdirSync(destAssets, { recursive: true });

  const ortFiles = [
    { src: "ort-wasm-simd-threaded.mjs", dest: "ort-wasm-simd-threaded.asyncify.mjs" },
    { src: "ort-wasm-simd-threaded.wasm", dest: "ort-wasm-simd-threaded.asyncify.wasm" },
    { src: "ort-wasm-simd-threaded.mjs", dest: "ort-wasm-simd-threaded.mjs" },
    { src: "ort-wasm-simd-threaded.wasm", dest: "ort-wasm-simd-threaded.wasm" },
    { src: "ort-wasm-simd-threaded.jsep.mjs", dest: "ort-wasm-simd-threaded.jsep.mjs" },
    { src: "ort-wasm-simd-threaded.jsep.wasm", dest: "ort-wasm-simd-threaded.jsep.wasm" },
  ];

  console.log("Copying ONNX Runtime Web WASM assets...");
  for (const file of ortFiles) {
    const srcPath = path.join(ortSrcDir, file.src);
    if (fs.existsSync(srcPath)) {
      // Copy to dist/
      fs.copyFileSync(srcPath, path.join(destRoot, file.dest));
      // Copy to dist/assets/
      fs.copyFileSync(srcPath, path.join(destAssets, file.dest));
      console.log(`Copied ${file.src} -> ${file.dest}`);
    } else {
      console.warn(`Warning: Source file not found: ${srcPath}`);
    }
  }
  console.log("ONNX Runtime Web WASM assets copied successfully!");
} catch (err) {
  console.error("Failed to copy framework or WASM assets:", err);
  process.exit(1);
}
