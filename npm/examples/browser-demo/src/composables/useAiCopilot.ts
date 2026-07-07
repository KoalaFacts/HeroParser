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
      const { pipeline, env, AutoConfig } = await import('@huggingface/transformers')
      
      env.allowLocalModels = false
      
      const progress_callback = (data: any) => {
        if (data.status === 'progress_total') {
          aiProgress.value = Math.floor(data.progress || 0)
          aiProgressLabel.value = `Downloading Gemma 4 weights: ${Math.floor(data.progress || 0)}%`
        } else if (data.status === 'ready') {
          aiProgressLabel.value = 'Preparing execution environment...'
        }
      }

      const modelId = 'tss-deposium/gemma-4-E2B-text-only-onnx-int4'

      aiProgressLabel.value = 'Loading model configuration...'
      const config = await AutoConfig.from_pretrained(modelId)
      // Override model_type to gemma2 so that transformers.js bypasses its unsupported gemma4_text check
      config.model_type = 'gemma2'

      try {
        aiProgressLabel.value = 'Initializing WebGPU accelerator...'
        generator = await pipeline('text-generation', modelId, {
          device: 'webgpu',
          dtype: 'q4',
          config,
          subfolder: 'onnx',
          model_file_name: 'decoder_model_merged',
          progress_callback
        })
        aiProgressLabel.value = 'Gemma 4 (E2B) Model loaded successfully in WebGPU memory!'
      } catch (gpuError) {
        console.warn("WebGPU initialization failed. Falling back to WebAssembly (CPU)...", gpuError)
        aiProgressLabel.value = 'WebGPU unsupported. Initializing WebAssembly CPU execution...'
        generator = await pipeline('text-generation', modelId, {
          device: 'wasm',
          dtype: 'q4',
          config,
          subfolder: 'onnx',
          model_file_name: 'decoder_model_merged',
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
    
    if (!generator) {
      aiOutput.value = 'Restoring Gemma 4 model from browser Cache API...'
      const modelId = 'tss-deposium/gemma-4-E2B-text-only-onnx-int4'
      try {
        const { pipeline, AutoConfig } = await import('@huggingface/transformers')
        const config = await AutoConfig.from_pretrained(modelId)
        config.model_type = 'gemma2'
        try {
          generator = await pipeline('text-generation', modelId, {
            device: 'webgpu',
            dtype: 'q4',
            config,
            subfolder: 'onnx',
            model_file_name: 'decoder_model_merged',
            local_files_only: true
          })
        } catch (gpuErr) {
          console.warn("WebGPU restore failed, falling back to WebAssembly (CPU)...", gpuErr)
          generator = await pipeline('text-generation', modelId, {
            device: 'wasm',
            dtype: 'q4',
            config,
            subfolder: 'onnx',
            model_file_name: 'decoder_model_merged',
            local_files_only: true
          })
        }
      } catch (e) {
        console.warn("Cache load failed, refetching...", e)
        const { pipeline, AutoConfig } = await import('@huggingface/transformers')
        const config = await AutoConfig.from_pretrained(modelId)
        config.model_type = 'gemma2'
        try {
          generator = await pipeline('text-generation', modelId, {
            device: 'webgpu',
            dtype: 'q4',
            config,
            subfolder: 'onnx',
            model_file_name: 'decoder_model_merged'
          })
        } catch (gpuErr) {
          generator = await pipeline('text-generation', modelId, {
            device: 'wasm',
            dtype: 'q4',
            config,
            subfolder: 'onnx',
            model_file_name: 'decoder_model_merged'
          })
        }
      }
    }

    aiOutput.value = 'Running local LLM inference on WebGPU device...'
    aiTime.value = 'Calculating...'
    aiTokensPerSec.value = 'Calculating...'

    const t0 = performance.now()
    try {
      const messages = [
        { role: 'system', content: 'You are a precise data parser. Convert the user input into a JSON array of objects. Each object must have keys: "Name", "Age" (as string or "Unknown"), and "Role" (as string or "Unknown"). Provide ONLY raw JSON inside the output, no markdown wrappers, no explanations.' },
        { role: 'user', content: aiInput.value }
      ]

      const output = await generator(messages, {
        max_new_tokens: 150,
        temperature: 0.1,
        return_full_text: false
      })

      const t1 = performance.now()
      const inferenceTime = t1 - t0
      
      let responseText = output[0].generated_text || ""
      responseText = responseText.replace(/```json/i, '').replace(/```/g, '').trim()

      aiOutput.value = responseText
      aiTime.value = `${inferenceTime.toFixed(2)} ms`

      const tokenCount = responseText.length / 4
      const tokensPerSec = (tokenCount / (inferenceTime / 1000)).toFixed(1)
      aiTokensPerSec.value = `${tokensPerSec} tok/sec`
    } catch (err: any) {
      console.error(err)
      aiOutput.value = `Inference failed: ${err.message}`
      aiTime.value = 'Error'
      aiTokensPerSec.value = 'Error'
    }
  }

  const clearAiCache = () => {
    localStorage.removeItem('gemma4_cached')
    aiModelLoaded.value = false
    aiProgress.value = 0
    aiProgressLabel.value = ''
    aiOutput.value = 'Click "Load AI Model" to download and initialize...'
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
    triggerDownloadWarning,
    cancelDownload,
    startModelDownload,
    runAiAgent,
    clearAiCache,
    checkCacheOnMount
  }
}
