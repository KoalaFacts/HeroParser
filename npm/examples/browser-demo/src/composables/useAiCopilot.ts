import { ref } from 'vue'

let generator: any = null
 
export function useAiCopilot() {
  const aiModelLoaded = ref(false)
  const aiLoading = ref(false)
  const aiProgress = ref(0)
  const aiProgressLabel = ref('')
  const aiInput = ref("Raw conversational unstructured data:\n\nName: John Doe, Age: 30, Occupation: Engineer\nName: Jane Smith, Age: 25, Occupation: Designer")
  const aiOutput = ref('Click "Load AI Model" to download and initialize...')
  const aiTime = ref('-')
  const aiTokensPerSec = ref('-')
  const showWarningModal = ref(false)
  const aiInferenceActive = ref(false)

  const triggerDownloadWarning = () => {
    if (aiLoading.value || aiModelLoaded.value) return
    showWarningModal.value = true
  }

  const cancelDownload = () => {
    showWarningModal.value = false
  }

  const startModelDownload = async () => {
    showWarningModal.value = false
    aiLoading.value = true
    aiProgress.value = 0
    aiProgressLabel.value = 'Initializing device and loading transformers library...'

    try {
      const { pipeline } = await import('@huggingface/transformers')
      
      const progress_callback = (data: any) => {
        if (data.status === 'progress_total') {
          aiProgress.value = Math.floor(data.progress || 0)
          aiProgressLabel.value = `Downloading Gemma 4 weights: ${Math.floor(data.progress || 0)}%`
        } else if (data.status === 'ready') {
          aiProgressLabel.value = 'Preparing execution environment...'
        }
      }

      const modelId = 'onnx-community/gemma-4-E2B-it-ONNX'

      try {
        aiProgressLabel.value = 'Initializing WebGPU accelerator...'
        generator = await pipeline('text-generation', modelId, {
          device: 'webgpu',
          dtype: 'q4',
          progress_callback
        })
        aiProgressLabel.value = 'Gemma 4 (E2B) Model loaded successfully in WebGPU memory!'
      } catch (gpuError) {
        console.warn("WebGPU initialization failed. Falling back to WebAssembly (CPU)...", gpuError)
        aiProgressLabel.value = 'WebGPU unsupported. Initializing WebAssembly CPU execution...'
        generator = await pipeline('text-generation', modelId, {
          device: 'wasm',
          dtype: 'q4',
          progress_callback
        })
        aiProgressLabel.value = 'Gemma 4 (E2B) Model loaded successfully in WebAssembly (CPU) memory!'
      }

      aiLoading.value = false
      aiModelLoaded.value = true
      localStorage.setItem('gemma4_cached', 'true')
      aiOutput.value = 'AI model initialized successfully. Type unstructured text and click "Run AI Agent" to parse it locally.'
    } catch (err: any) {
      console.error(err)
      aiLoading.value = false
      const errMsg = err.message || String(err)
      aiProgressLabel.value = `Initialization failed: ${errMsg}`
      aiOutput.value = `Error loading AI model: ${errMsg}. Please ensure you have a stable network connection.`
    }
  }

  const runAiAgent = async () => {
    if (!aiModelLoaded.value) return
    if (aiInferenceActive.value || aiLoading.value) return
    
    if (!generator) {
      aiLoading.value = true
      aiProgress.value = 100
      aiOutput.value = 'Restoring Gemma 4 model from browser Cache API...'
      aiProgressLabel.value = 'Loading cached model files...'
      
      const modelId = 'onnx-community/gemma-4-E2B-it-ONNX'
      try {
        const { pipeline } = await import('@huggingface/transformers')
        try {
          generator = await pipeline('text-generation', modelId, {
            device: 'webgpu',
            dtype: 'q4',
            local_files_only: true
          })
        } catch (gpuErr) {
          console.warn("WebGPU restore failed, falling back to WebAssembly (CPU)...", gpuErr)
          generator = await pipeline('text-generation', modelId, {
            device: 'wasm',
            dtype: 'q4',
            local_files_only: true
          })
        }
        aiProgressLabel.value = 'Model restored successfully!'
      } catch (e) {
        console.warn("Cache restore failed", e)
        aiOutput.value = 'Failed to load model from browser cache. The model files might have been evicted by the browser. Please reload the model using the "Load AI Model" button.'
        aiProgressLabel.value = 'Cache restore failed.'
        aiModelLoaded.value = false
        localStorage.removeItem('gemma4_cached')
        aiLoading.value = false
        return
      }
      aiLoading.value = false
    }

    aiInferenceActive.value = true
    aiOutput.value = '' // Clear output box to stream in real-time
    aiTime.value = 'Calculating...'
    aiTokensPerSec.value = 'Calculating...'

    const t0 = performance.now()
    try {
      const { TextStreamer } = await import('@huggingface/transformers')

      // Instantiate a TextStreamer to output tokens as they are generated
      const streamer = new TextStreamer(generator.tokenizer, {
        skip_prompt: true,
        skip_special_tokens: true,
        callback_function: (text: string) => {
          aiOutput.value += text
        }
      })

      const messages = [
        { role: 'system', content: 'You are a precise data parser. Convert the user input into a JSON array of objects. Each object must have keys: "Name", "Age" (as string or "Unknown"), and "Role" (as string or "Unknown"). Provide ONLY raw JSON inside the output, no markdown wrappers, no explanations.' },
        { role: 'user', content: aiInput.value }
      ]

      const output = await generator(messages, {
        max_new_tokens: 150,
        temperature: 0.1,
        return_full_text: false,
        streamer
      })

      const t1 = performance.now()
      const inferenceTime = t1 - t0
      
      let responseText = output[0].generated_text || ""
      responseText = responseText.replace(/```json/i, '').replace(/```/g, '').trim()

      aiOutput.value = responseText
      aiTime.value = `${inferenceTime.toFixed(2)} ms`

      // Measure exact token count using the tokenizer
      const tokens = generator.tokenizer.encode(responseText)
      const tokenCount = tokens.length
      const tokensPerSec = (tokenCount / (inferenceTime / 1000)).toFixed(1)
      aiTokensPerSec.value = `${tokensPerSec} tok/sec`
    } catch (err: any) {
      console.error(err)
      aiOutput.value = `Inference failed: ${err.message}`
      aiTime.value = 'Error'
      aiTokensPerSec.value = 'Error'
    } finally {
      aiInferenceActive.value = false
    }
  }

  const clearAiCache = async () => {
    localStorage.removeItem('gemma4_cached')
    aiModelLoaded.value = false
    aiProgress.value = 0
    aiProgressLabel.value = ''
    aiOutput.value = 'Click "Load AI Model" to download and initialize...'
    
    // Dereference generator to allow GC
    generator = null

    // Delete model files from browser cache storage
    if (typeof caches !== 'undefined') {
      try {
        aiOutput.value = 'Clearing model cache storage...'
        const deleted = await caches.delete('transformers-cache')
        if (deleted) {
          aiOutput.value = 'Cache cleared successfully. All model files deleted from browser Cache storage.'
        } else {
          aiOutput.value = 'Cache metadata cleared. Storage was already empty.'
        }
      } catch (err: any) {
        console.warn('Failed to delete cache storage:', err)
        aiOutput.value = 'Metadata cleared, but failed to delete files from Cache Storage: ' + err.message
      }
    }
  }

  const checkCacheOnMount = () => {
    if (localStorage.getItem('gemma4_cached') === 'true') {
      aiModelLoaded.value = true
      aiProgressLabel.value = 'Gemma 4 model loaded from local cache.'
      aiOutput.value = 'AI Assistant is ready. Enter unstructured text and click "Run AI Agent" to parse it locally via WebGPU.'
    }
  }

  return {
    aiModelLoaded,
    aiLoading,
    aiProgress,
    aiProgressLabel,
    aiInput,
    aiOutput,
    aiTime,
    aiTokensPerSec,
    showWarningModal,
    aiInferenceActive,
    triggerDownloadWarning,
    cancelDownload,
    startModelDownload,
    runAiAgent,
    clearAiCache,
    checkCacheOnMount
  }
}
