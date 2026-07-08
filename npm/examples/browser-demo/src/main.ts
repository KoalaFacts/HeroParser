// WebGPU buffer limit patch: Override GPUAdapter.prototype.requestDevice at the absolute entry point
// to request maximum buffer sizes (up to 2GB) before any libraries or modules are initialized.
if (typeof GPUAdapter !== 'undefined') {
  const originalRequestDevice = GPUAdapter.prototype.requestDevice
  GPUAdapter.prototype.requestDevice = function (descriptor: any) {
    descriptor = descriptor || {}
    const requiredLimits = descriptor.requiredLimits || {}
    if (this.limits) {
      requiredLimits.maxBufferSize = Math.min(this.limits.maxBufferSize || 256 * 1024 * 1024, 2147483648)
      requiredLimits.maxStorageBufferBindingSize = Math.min(this.limits.maxStorageBufferBindingSize || 256 * 1024 * 1024, 2147483648)
    }
    descriptor.requiredLimits = requiredLimits
    return originalRequestDevice.call(this, descriptor)
  }
}


// URL quote-cleansing patch: Intercept fetch and XMLHttpRequest to strip literal double quotes
// from all Hugging Face ONNX external data requests, resolving exporter formatting bugs.
const cleanUrl = (url: any) => {
  if (typeof url === 'string' && url.includes('huggingface.co') && (url.includes('%22') || url.includes('"'))) {
    return url.replace(/%22/g, '').replace(/"/g, '')
  }
  return url
}

if (typeof window !== 'undefined') {
  const originalFetch = window.fetch
  window.fetch = function (input: any, init: any) {
    if (typeof input === 'string') {
      input = cleanUrl(input)
    } else if (input && typeof input === 'object' && 'url' in input) {
      const cleanedUrl = cleanUrl(input.url)
      if (cleanedUrl !== input.url) {
        input = new Request(cleanedUrl, input)
      }
    }
    return originalFetch.call(this, input, init)
  }

  const originalOpen = XMLHttpRequest.prototype.open
  XMLHttpRequest.prototype.open = function (method: string, url: any, ...args: any[]) {
    if (typeof url === 'string') {
      url = cleanUrl(url)
    }
    return (originalOpen as any).call(this, method, url, ...args)
  }
}

// Session routing patch: Intercept global ORT InferenceSession creation to dynamically
// load embed_tokens on CPU/WASM and decoder layers on WebGPU.
const applyOrtPatch = (ortObj: any) => {
  if (ortObj && ortObj.InferenceSession && !ortObj.InferenceSession.patched) {
    const originalCreate = ortObj.InferenceSession.create
    ortObj.InferenceSession.create = async function (modelData: any, options: any) {
      options = options || {}
      
      let isEmbedTokens = false
      if (modelData) {
        try {
          let bytes: Uint8Array | null = null
          if (modelData instanceof Uint8Array) {
            bytes = modelData.subarray(0, 4000)
          } else if (modelData instanceof ArrayBuffer) {
            bytes = new Uint8Array(modelData, 0, Math.min(modelData.byteLength, 4000))
          }
          if (bytes) {
            const decoder = new TextDecoder('utf-8')
            const text = decoder.decode(bytes)
            if (text.includes('embed_tokens') || text.includes('GatherBlockQuantized') || text.includes('Gather_Quant')) {
              isEmbedTokens = true
            }
          }
        } catch (e) {
          console.warn('Failed to parse model bytes for session routing:', e)
        }
      }

      if (isEmbedTokens) {
        console.log('[Routing] Routing embed_tokens session to WASM execution provider (CPU)...')
        options.executionProviders = ['wasm']
      } else {
        console.log('[Routing] Routing session to WebGPU execution provider...')
        options.executionProviders = ['webgpu']
      }
      
      return originalCreate.call(this, modelData, options)
    }
    ortObj.InferenceSession.patched = true
    console.log('[Routing] ONNX Runtime InferenceSession successfully patched!')
  }
}

if (typeof globalThis !== 'undefined') {
  let currentOrt = (globalThis as any).ort
  if (currentOrt) {
    applyOrtPatch(currentOrt)
  }
  Object.defineProperty(globalThis, 'ort', {
    get() {
      return currentOrt
    },
    set(val) {
      applyOrtPatch(val)
      currentOrt = val
    },
    configurable: true
  })
}

import { createVaporApp } from 'vue'
import App from './App.vue'
import './style.css'

// Mount our Vue Vapor application (bypassing Virtual DOM entirely)
createVaporApp(App as any).mount('#app')
