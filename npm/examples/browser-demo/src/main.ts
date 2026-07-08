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

import { createVaporApp } from 'vue'
import App from './App.vue'
import './style.css'

// Mount our Vue Vapor application (bypassing Virtual DOM entirely)
createVaporApp(App as any).mount('#app')
