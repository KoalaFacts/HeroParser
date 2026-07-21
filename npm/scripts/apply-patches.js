import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Target node_modules directory
const rootDir = path.resolve(__dirname, '../node_modules/onnxruntime-web/dist');

const quotePatch = `if (typeof window !== 'undefined') {
  const cleanUrl = (url) => {
    if (typeof url === 'string' && url.includes('huggingface.co') && (url.includes('%22') || url.includes('"'))) {
      return url.replace(/%22/g, '').replace(/"/g, '');
    }
    return url;
  };
  if (!window._quoteCleanPatched) {
    window._quoteCleanPatched = true;
    const originalFetch = window.fetch;
    window.fetch = function (input, init) {
      if (typeof input === 'string') {
        input = cleanUrl(input);
      } else if (input && typeof input === 'object' && 'url' in input) {
        const cleanedUrl = cleanUrl(input.url);
        if (cleanedUrl !== input.url) {
          input = new Request(cleanedUrl, input);
        }
      }
      return originalFetch.call(this, input, init);
    };
    const originalOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function (method, url, ...args) {
      if (typeof url === 'string') {
        url = cleanUrl(url);
      }
      return originalOpen.call(this, method, url, ...args);
    };
  }
}
`;

// 1. Patch WebGPU Bundle
const webgpuPath = path.join(rootDir, 'ort.webgpu.bundle.min.mjs');
if (fs.existsSync(webgpuPath)) {
  console.log('Patching ort.webgpu.bundle.min.mjs...');
  let content = fs.readFileSync(webgpuPath, 'utf8');

  // Insert quote patch at the very top
  if (!content.includes('_quoteCleanPatched')) {
    content = quotePatch + content;
  }

  // Insert InferenceSession.create patch
  const target = 'let[d,l]=await _r(a),c=await d.createInferenceSessionHandler(i,l);';
  const patchCode = `
      // [PATCHED LOGIC]
      let isEmbedTokens = false;
      if (i instanceof Uint8Array) {
        try {
          const text = new TextDecoder('utf-8').decode(i.subarray(0, 4000));
          if (text.includes('embed_tokens') || text.includes('GatherBlockQuantized') || text.includes('Gather_Quant')) {
            isEmbedTokens = true;
          }
        } catch(e){}
      } else if (typeof i === 'string') {
        if (i.includes('embed_tokens')) {
          isEmbedTokens = true;
        }
      }

      let wantsWasm = false;
      if (a && Array.isArray(a.executionProviders)) {
        wantsWasm = a.executionProviders.includes('wasm') || a.executionProviders.includes('cpu');
      }

      if (isEmbedTokens) {
        console.log('[ort-patch] Routing embed_tokens to WASM provider');
        let extBytes = null;
        try {
          const extUrl = "https://huggingface.co/onnx-community/gemma-4-E2B-it-ONNX/resolve/main/onnx/embed_tokens_q4.onnx_data";
          console.log('[ort-patch] Fetching external weights for embed_tokens from:', extUrl);
          const res = await fetch(extUrl);
          if (res.ok) {
            extBytes = new Uint8Array(await res.arrayBuffer());
            console.log('[ort-patch] Successfully fetched external weights, size:', extBytes.byteLength);
          }
        } catch (err) {
          console.error('[ort-patch] Failed to fetch external weights:', err);
        }

        const cleanOpts = { executionProviders: ['wasm'] };
        if (extBytes) {
          cleanOpts.externalData = [
            { data: extBytes, path: 'embed_tokens_q4.onnx_data' },
            { data: extBytes, path: './embed_tokens_q4.onnx_data' }
          ];
        }
        if (a) {
          if (a.logSeverityLevel !== undefined) cleanOpts.logSeverityLevel = a.logSeverityLevel;
          if (a.logVerbosityLevel !== undefined) cleanOpts.logVerbosityLevel = a.logVerbosityLevel;
        }
        a = cleanOpts;
      } else if (wantsWasm) {
        console.log('[ort-patch] Respecting caller request for WASM');
      } else {
        console.log('[ort-patch] Routing session to WebGPU');
        a.executionProviders = ['webgpu'];
      }

      if (a && typeof a === 'object') {
        for (const key in a) {
          if (Object.prototype.hasOwnProperty.call(a, key)) {
            const val = a[key];
            if (typeof val === 'string' && /^\\d+$/.test(val)) {
              a[key] = parseInt(val, 10);
            }
          }
        }
        if (Array.isArray(a.executionProviders)) {
          for (const ep of a.executionProviders) {
            if (ep && typeof ep === 'object') {
              for (const k in ep) {
                if (Object.prototype.hasOwnProperty.call(ep, k)) {
                  const v = ep[k];
                  if (typeof v === 'string' && /^\\d+$/.test(v)) {
                    ep[k] = parseInt(v, 10);
                  }
                }
              }
            }
          }
        }
      }
  `;

  if (content.includes(target) && !content.includes('[PATCHED LOGIC]')) {
    content = content.replace(target, patchCode + target);
    fs.writeFileSync(webgpuPath, content, 'utf8');
    console.log('Successfully patched ort.webgpu.bundle.min.mjs!');
  } else {
    console.log('ort.webgpu.bundle.min.mjs already patched or target not found.');
  }
}

// 2. Patch CPU Bundle
const cpuPath = path.join(rootDir, 'ort.bundle.min.mjs');
if (fs.existsSync(cpuPath)) {
  console.log('Patching ort.bundle.min.mjs...');
  let content = fs.readFileSync(cpuPath, 'utf8');

  // Insert quote patch at the very top
  if (!content.includes('_quoteCleanPatched')) {
    content = quotePatch + content;
  }

  // Insert InferenceSession.create patch
  const target = 'let[a,u]=await ln(s),l=await a.createInferenceSessionHandler(n,u);';
  const patchCode = `
      // [PATCHED LOGIC]
      let isEmbedTokens = false;
      if (n instanceof Uint8Array) {
        try {
          const text = new TextDecoder('utf-8').decode(n.subarray(0, 4000));
          if (text.includes('embed_tokens') || text.includes('GatherBlockQuantized') || text.includes('Gather_Quant')) {
            isEmbedTokens = true;
          }
        } catch(e){}
      } else if (typeof n === 'string') {
        if (n.includes('embed_tokens')) {
          isEmbedTokens = true;
        }
      }

      if (isEmbedTokens) {
        console.log('[ort-patch] Loading external weights for embed_tokens');
        let extBytes = null;
        try {
          const extUrl = "https://huggingface.co/onnx-community/gemma-4-E2B-it-ONNX/resolve/main/onnx/embed_tokens_q4.onnx_data";
          const res = await fetch(extUrl);
          if (res.ok) {
            extBytes = new Uint8Array(await res.arrayBuffer());
          }
        } catch (err) {}

        const cleanOpts = { executionProviders: ['wasm'] };
        if (extBytes) {
          cleanOpts.externalData = [
            { data: extBytes, path: 'embed_tokens_q4.onnx_data' },
            { data: extBytes, path: './embed_tokens_q4.onnx_data' }
          ];
        }
        if (s) {
          if (s.logSeverityLevel !== undefined) cleanOpts.logSeverityLevel = s.logSeverityLevel;
          if (s.logVerbosityLevel !== undefined) cleanOpts.logVerbosityLevel = s.logVerbosityLevel;
        }
        s = cleanOpts;
      }

      if (s && typeof s === 'object') {
        for (const key in s) {
          if (Object.prototype.hasOwnProperty.call(s, key)) {
            const val = s[key];
            if (typeof val === 'string' && /^\\d+$/.test(val)) {
              s[key] = parseInt(val, 10);
            }
          }
        }
        if (Array.isArray(s.executionProviders)) {
          for (const ep of s.executionProviders) {
            if (ep && typeof ep === 'object') {
              for (const k in ep) {
                if (Object.prototype.hasOwnProperty.call(ep, k)) {
                  const v = ep[k];
                  if (typeof v === 'string' && /^\\d+$/.test(v)) {
                    ep[k] = parseInt(v, 10);
                  }
                }
              }
            }
          }
        }
      }
  `;

  if (content.includes(target) && !content.includes('[PATCHED LOGIC]')) {
    content = content.replace(target, patchCode + target);
    fs.writeFileSync(cpuPath, content, 'utf8');
    console.log('Successfully patched ort.bundle.min.mjs!');
  } else {
    console.log('ort.bundle.min.mjs already patched or target not found.');
  }
}
