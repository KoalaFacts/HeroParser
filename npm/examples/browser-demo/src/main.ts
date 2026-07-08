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
// from tss-deposium model file requests, resolving the model's metadata export bugs.
const cleanUrl = (url: any) => {
  if (typeof url === 'string' && url.includes('tss-deposium/gemma-4-E2B-text-only-onnx-int4')) {
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

import { createVaporApp } from 'vue'
import App from './App.vue'
import './style.css'

// Mount our Vue Vapor application (bypassing Virtual DOM entirely)
createVaporApp(App as any).mount('#app')
