<script setup vapor>
import { ref, onMounted } from 'vue'
import { init, readCsv, detectCsvDelimiter, readFixedWidth, readExcel } from 'heroparser'

// Bootstrap state
const initialized = ref(false)
const initStatus = ref('Initializing HeroParser WASM Engine...')

// Tab selection
const activeTab = ref('csv')
const switchTab = (tab) => {
  activeTab.value = tab
}

// CSV state
const csvInput = ref("Name,Age,Role\nAlice,30,Developer\nBob,25,Designer")
const csvDelimiter = ref(",")
const csvHasHeader = ref(true)
const csvOutput = ref('Click "Parse CSV" to see results...')
const csvTime = ref('-')
const csvCount = ref('-')

// Fixed-Width state
const fwInput = ref("Alice     30        Developer \nBob       25        Designer  ")
const fwSpecs = ref(`[
  { "name": "Name", "start": 0, "length": 10 },
  { "name": "Age", "start": 10, "length": 10 },
  { "name": "Role", "start": 20, "length": 11 }
]`)
const fwOutput = ref('Click "Parse Fixed-Width" to see results...')
const fwTime = ref('-')
const fwCount = ref('-')

// Excel state
const excelSheetName = ref("")
const excelHasHeader = ref(true)
const excelFileInfo = ref("")
const excelError = ref("")
const excelOutput = ref('Upload an Excel workbook and click "Parse Excel" to inspect contents...')
const excelTime = ref('-')
const excelCount = ref('-')
let loadedExcelBytes = null
let generator = null

// AI state variables
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
    const { pipeline, env, AutoConfig, AutoModelForCausalLM, AutoTokenizer } = await import('@huggingface/transformers')
    
    // Disable local-only lookups initially to fetch from Hugging Face
    env.allowLocalModels = false
    
    const progress_callback = (data) => {
      if (data.status === 'progress_total') {
        aiProgress.value = Math.floor(data.progress || 0)
        aiProgressLabel.value = `Downloading Gemma 4 weights: ${Math.floor(data.progress || 0)}%`
      } else if (data.status === 'ready') {
        aiProgressLabel.value = 'Preparing execution environment...'
      }
    }

    const modelId = 'onnx-community/gemma-4-E2B-it-ONNX'
    
    aiProgressLabel.value = 'Loading configuration and tokenizer...'
    const config = await AutoConfig.from_pretrained(modelId)
    // Override the model_type to point to gemma2 architecture to bypass transformers.js unsupported type check
    config.model_type = 'gemma2'
    
    const tokenizer = await AutoTokenizer.from_pretrained(modelId)
    let model;

    try {
      aiProgressLabel.value = 'Initializing WebGPU accelerator...'
      // Try WebGPU first
      model = await AutoModelForCausalLM.from_pretrained(modelId, {
        config,
        device: 'webgpu',
        progress_callback
      })
      aiProgressLabel.value = 'Gemma 4 (E2B) Model loaded successfully in WebGPU memory!'
    } catch (gpuError) {
      console.warn("WebGPU initialization failed. Falling back to WebAssembly (CPU)...", gpuError)
      aiProgressLabel.value = 'WebGPU unsupported. Initializing WebAssembly CPU execution...'
      // Fallback to CPU (wasm)
      model = await AutoModelForCausalLM.from_pretrained(modelId, {
        config,
        device: 'wasm',
        progress_callback
      })
      aiProgressLabel.value = 'Gemma 4 (E2B) Model loaded successfully in WebAssembly (CPU) memory!'
    }

    // Build the generator pipeline around our preloaded model and tokenizer
    generator = await pipeline('text-generation', model, {
      tokenizer
    })

    aiLoading.value = false
    aiModelLoaded.value = true
    localStorage.setItem('gemma4_cached', 'true')
    aiOutput.value = 'AI model initialized successfully. Type unstructured text and click "Run AI Agent" to parse it locally.'
  } catch (err) {
    console.error(err)
    aiLoading.value = false
    const errMsg = err.message || String(err)
    aiProgressLabel.value = `Initialization failed: ${errMsg}`
    aiOutput.value = `Error loading AI model: ${errMsg}. Please ensure you have a stable network connection.`
  }
}

const runAiAgent = async () => {
  if (!aiModelLoaded.value) return
  
  // Lazily restore pipeline if cached flag was set but generator wasn't initialized in memory yet
  if (!generator) {
    aiOutput.value = 'Restoring Gemma 4 model from browser Cache API...'
    try {
      const { pipeline, AutoConfig, AutoModelForCausalLM, AutoTokenizer } = await import('@huggingface/transformers')
      const modelId = 'onnx-community/gemma-4-E2B-it-ONNX'
      const config = await AutoConfig.from_pretrained(modelId)
      config.model_type = 'gemma2'
      
      const tokenizer = await AutoTokenizer.from_pretrained(modelId)
      let model;
      try {
        model = await AutoModelForCausalLM.from_pretrained(modelId, {
          config,
          device: 'webgpu',
          local_files_only: true
        })
      } catch (gpuErr) {
        console.warn("WebGPU restore failed, falling back to WebAssembly (CPU)...", gpuErr)
        model = await AutoModelForCausalLM.from_pretrained(modelId, {
          config,
          device: 'wasm',
          local_files_only: true
        })
      }
      generator = await pipeline('text-generation', model, {
        tokenizer
      })
    } catch (e) {
      console.warn("Cache load failed, refetching...", e)
      const { pipeline, AutoConfig, AutoModelForCausalLM, AutoTokenizer } = await import('@huggingface/transformers')
      const modelId = 'onnx-community/gemma-4-E2B-it-ONNX'
      const config = await AutoConfig.from_pretrained(modelId)
      config.model_type = 'gemma2'
      
      const tokenizer = await AutoTokenizer.from_pretrained(modelId)
      let model;
      try {
        model = await AutoModelForCausalLM.from_pretrained(modelId, {
          config,
          device: 'webgpu'
        })
      } catch (gpuErr) {
        model = await AutoModelForCausalLM.from_pretrained(modelId, {
          config,
          device: 'wasm'
        })
      }
      generator = await pipeline('text-generation', model, {
        tokenizer
      })
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
    // Clean markdown code blocks if the model generated them despite instructions
    responseText = responseText.replace(/```json/i, '').replace(/```/g, '').trim()

    aiOutput.value = responseText
    aiTime.value = `${inferenceTime.toFixed(2)} ms`

    const tokenCount = responseText.length / 4
    const tokensPerSec = (tokenCount / (inferenceTime / 1000)).toFixed(1)
    aiTokensPerSec.value = `${tokensPerSec} tok/sec`
  } catch (err) {
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

onMounted(async () => {
  if (localStorage.getItem('gemma4_cached') === 'true') {
    aiModelLoaded.value = true
    aiProgressLabel.value = 'Gemma 4 model loaded from local cache.'
    aiOutput.value = 'AI Assistant is ready. Enter unstructured text and click "Run AI Agent" to parse it locally via WebGPU.'
  }
  try {
    console.log("Bootstrapping WASM inside Vue Vapor SFC...")
    await init()
    console.log("WASM Initialized successfully!")
    initialized.value = true
  } catch (err) {
    console.error("Initialization failed:", err)
    initStatus.value = 'Failed to load WASM engine. Check dev console logs.'
  }
})

// Actions
const runCsvParse = () => {
  const t0 = performance.now()
  try {
    const result = readCsv(csvInput.value, { 
      delimiter: csvDelimiter.value || ",", 
      hasHeader: csvHasHeader.value 
    })
    const t1 = performance.now()
    csvOutput.value = JSON.stringify(result, null, 2)
    csvTime.value = `${(t1 - t0).toFixed(2)} ms`
    csvCount.value = result.length.toString()
  } catch (err) {
    csvOutput.value = `Parsing Error: ${err.message}`
  }
}

const runCsvDelimiterDetect = () => {
  const t0 = performance.now()
  try {
    const delim = detectCsvDelimiter(csvInput.value)
    const t1 = performance.now()
    csvOutput.value = `Detected delimiter character: "${delim}"\n(Found in ${(t1 - t0).toFixed(2)} ms)`
    csvTime.value = `${(t1 - t0).toFixed(2)} ms`
    csvCount.value = '-'
  } catch (err) {
    csvOutput.value = `Detection Error: ${err.message}`
  }
}

const runFixedWidthParse = () => {
  let specs
  try {
    specs = JSON.parse(fwSpecs.value)
  } catch (err) {
    fwOutput.value = `Error: Invalid specification JSON format.\n${err.message}`
    return
  }

  const t0 = performance.now()
  try {
    const result = readFixedWidth(fwInput.value, specs)
    const t1 = performance.now()
    fwOutput.value = JSON.stringify(result, null, 2)
    fwTime.value = `${(t1 - t0).toFixed(2)} ms`
    fwCount.value = result.length.toString()
  } catch (err) {
    fwOutput.value = `Parsing Error: ${err.message}`
  }
}

// Excel dropzone event handlers
const handleExcelSelect = (e) => {
  const files = e.target.files
  if (files && files.length > 0) {
    loadExcelFile(files[0])
  }
}

const handleExcelDrop = (e) => {
  e.preventDefault()
  const files = e.dataTransfer.files
  if (files && files.length > 0 && files[0].name.endsWith('.xlsx')) {
    loadExcelFile(files[0])
  }
}

const loadExcelFile = (file) => {
  excelError.value = ""
  const reader = new FileReader()
  reader.onload = (e) => {
    loadedExcelBytes = new Uint8Array(e.target.result)
    excelFileInfo.value = `Loaded: ${file.name} (${(file.size / 1024).toFixed(1)} KB)`
  }
  reader.readAsArrayBuffer(file)
}

const runExcelParse = () => {
  if (!loadedExcelBytes) return
  excelError.value = ""

  const t0 = performance.now()
  try {
    const result = readExcel(loadedExcelBytes, excelSheetName.value || "", excelHasHeader.value)
    const t1 = performance.now()
    excelOutput.value = JSON.stringify(result, null, 2)
    excelTime.value = `${(t1 - t0).toFixed(2)} ms`
    excelCount.value = result.length.toString()
  } catch (err) {
    console.error(err)
    excelError.value = `WASM Parsing Error: ${err.message}`
  }
}
const faviconUrl = './favicon.svg'
const iconsUrl = './icons.svg'
</script>

<template>
  <!-- Loading Overlay -->
  <div v-if="!initialized" id="init-overlay">
    <div class="spinner"></div>
    <div id="init-status">{{ initStatus }}</div>
  </div>

  <div class="container">
    <div style="display: flex; justify-content: flex-end; gap: 1.5rem; margin-bottom: 1.5rem; font-size: 0.95rem;">
      <a href="https://github.com/KoalaFacts/HeroParser" target="_blank" style="display: flex; align-items: center; gap: 0.4rem; color: var(--text-muted); text-decoration: none; transition: color 0.2s;" onmouseover="this.style.color='var(--text)'" onmouseout="this.style.color='var(--text-muted)'">
        <svg style="width: 18px; height: 18px; fill: currentColor;"><use :href="`${iconsUrl}#github-icon`"></use></svg>
        GitHub
      </a>
      <a href="https://github.com/KoalaFacts/HeroParser#readme" target="_blank" style="display: flex; align-items: center; gap: 0.4rem; color: var(--text-muted); text-decoration: none; transition: color 0.2s;" onmouseover="this.style.color='var(--text)'" onmouseout="this.style.color='var(--text-muted)'">
        <svg style="width: 18px; height: 18px; fill: none; stroke: currentColor;"><use :href="`${iconsUrl}#documentation-icon`"></use></svg>
        Documentation
      </a>
    </div>

    <header>
      <div style="display: flex; align-items: center; justify-content: center; gap: 1rem; margin-bottom: 0.5rem;">
        <img :src="faviconUrl" alt="HeroParser Logo" style="width: 48px; height: 48px;" />
        <h1 style="margin: 0;">HeroParser WASM</h1>
      </div>
      <p class="tagline">High-performance, zero-allocation C# tabular parser running at native speed directly inside a browser.</p>
    </header>

    <!-- Tabs Nav -->
    <div class="tabs-nav">
      <button :class="['tab-btn', { active: activeTab === 'csv' }]" @click="switchTab('csv')">CSV Parser</button>
      <button :class="['tab-btn', { active: activeTab === 'fixedwidth' }]" @click="switchTab('fixedwidth')">Fixed-Width</button>
      <button :class="['tab-btn', { active: activeTab === 'excel' }]" @click="switchTab('excel')">Excel (.xlsx)</button>
      <button :class="['tab-btn', { active: activeTab === 'ai' }]" @click="switchTab('ai')">AI Copilot (WebGPU)</button>
    </div>

    <!-- CSV Content -->
    <div v-if="activeTab === 'csv'" id="tab-csv" class="tab-content active">
      <div class="panel">
        <div class="panel-title">CSV Input</div>
        <div class="form-group">
          <label for="csv-input">CSV Text Data</label>
          <textarea id="csv-input" v-model="csvInput" placeholder="Name,Age,Role"></textarea>
        </div>
        <div class="options-grid">
          <div class="form-group">
            <label for="csv-delimiter">Delimiter</label>
            <input type="text" id="csv-delimiter" v-model="csvDelimiter" maxlength="1">
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem; margin-top: 1.5rem;">
            <input type="checkbox" id="csv-header" v-model="csvHasHeader" style="width: 18px; height: 18px; accent-color: var(--primary);">
            <label for="csv-header" style="cursor: pointer;">Has Header Row</label>
          </div>
        </div>
        <div class="options-grid">
          <button class="btn" @click="runCsvParse">Parse CSV</button>
          <button class="btn btn-secondary" @click="runCsvDelimiterDetect">Detect Delimiter</button>
        </div>
      </div>
      <div class="panel">
        <div class="panel-title">JSON Output</div>
        <div class="output-container">
          <pre id="csv-output" class="output-pre">{{ csvOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Parse Time: <span id="csv-metric-time" class="metric-value">{{ csvTime }}</span></div>
          <div>Records: <span id="csv-metric-count" class="metric-value">{{ csvCount }}</span></div>
        </div>
      </div>
    </div>

    <!-- Fixed-Width Content -->
    <div v-if="activeTab === 'fixedwidth'" id="tab-fixedwidth" class="tab-content active">
      <div class="panel">
        <div class="panel-title">Fixed-Width Input</div>
        <div class="form-group">
          <label for="fw-input">Fixed-Width Text Data</label>
          <textarea id="fw-input" v-model="fwInput" placeholder="Alice     30        Developer"></textarea>
        </div>
        <div class="form-group">
          <label for="fw-specs">Column Ranges (JSON Specification)</label>
          <textarea id="fw-specs" v-model="fwSpecs" style="min-height: 120px;"></textarea>
        </div>
        <button class="btn" @click="runFixedWidthParse">Parse Fixed-Width</button>
      </div>
      <div class="panel">
        <div class="panel-title">JSON Output</div>
        <div class="output-container">
          <pre id="fw-output" class="output-pre">{{ fwOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Parse Time: <span id="fw-metric-time" class="metric-value">{{ fwTime }}</span></div>
          <div>Records: <span id="fw-metric-count" class="metric-value">{{ fwCount }}</span></div>
        </div>
      </div>
    </div>

    <!-- Excel Content -->
    <div v-if="activeTab === 'excel'" id="tab-excel" class="tab-content active">
      <div class="panel">
        <div class="panel-title">Excel (.xlsx) Upload</div>
        <div id="excel-dropzone" class="dropzone" @click="document.getElementById('excel-file').click()" @dragover.prevent @drop.prevent="handleExcelDrop">
          <span class="dropzone-icon">📥</span>
          <span class="dropzone-text">Drag and drop an Excel (.xlsx) file here, or click to browse</span>
          <span v-if="excelFileInfo" id="excel-file-info" class="dropzone-file-info" style="display: block;">{{ excelFileInfo }}</span>
          <input type="file" id="excel-file" accept=".xlsx" style="display: none;" @change="handleExcelSelect">
        </div>
        <div class="options-grid">
          <div class="form-group">
            <label for="excel-sheet">Sheet Name (leave empty for first sheet)</label>
            <input type="text" id="excel-sheet" v-model="excelSheetName" placeholder="Sheet1">
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem; margin-top: 1.5rem;">
            <input type="checkbox" id="excel-header" v-model="excelHasHeader" style="width: 18px; height: 18px; accent-color: var(--primary);">
            <label for="excel-header" style="cursor: pointer;">Has Header Row</label>
          </div>
        </div>
        <button class="btn" id="btn-parse-excel" @click="runExcelParse" :disabled="!excelFileInfo">Parse Excel Sheet</button>
        <div v-if="excelError" id="excel-error" class="alert-error" style="display: block;">{{ excelError }}</div>
      </div>
      <div class="panel">
        <div class="panel-title">JSON Output</div>
        <div class="output-container">
          <pre id="excel-output" class="output-pre">{{ excelOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Parse Time: <span id="excel-metric-time" class="metric-value">{{ excelTime }}</span></div>
          <div>Records: <span id="excel-metric-count" class="metric-value">{{ excelCount }}</span></div>
        </div>
      </div>
    </div>
    <!-- AI Copilot Content (WebGPU Lazy-loaded) -->
    <div v-if="activeTab === 'ai'" id="tab-ai" class="tab-content active">
      <div class="panel">
        <div class="panel-title">Local AI Configuration (WebGPU)</div>
        
        <div class="ai-card">
          <div style="display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--border); padding-bottom: 0.75rem; margin-bottom: 0.25rem;">
            <span style="font-weight: 600; font-size: 0.95rem;">Gemma 4 (E2B) AI Model</span>
            <span style="color: var(--text-muted); font-size: 0.85rem;">~1.1 GB (Quantized)</span>
          </div>

          <div style="display: flex; flex-direction: column; gap: 0.5rem;">
            <div style="display: flex; justify-content: space-between; align-items: center;">
              <span class="status-badge" :class="aiModelLoaded ? 'ready' : (aiLoading ? 'loading' : 'not-loaded')">
                <span v-if="aiModelLoaded">🟢 Loaded & Ready</span>
                <span v-else-if="aiLoading">🟡 Downloading...</span>
                <span v-else>⚪ Not Loaded</span>
              </span>
              <button v-if="!aiModelLoaded" class="btn btn-secondary" @click="triggerDownloadWarning" :disabled="aiLoading" style="padding: 0.5rem 1rem; font-size: 0.85rem; box-shadow: none;">
                Load AI Model
              </button>
              <button v-else class="btn btn-secondary" @click="clearAiCache" style="padding: 0.5rem 1rem; font-size: 0.85rem; box-shadow: none; border-color: rgba(239, 68, 68, 0.3); color: #fca5a5;">
                Clear Cache
              </button>
            </div>
            
            <div v-if="aiLoading || aiModelLoaded" class="progress-container">
              <div class="progress-bar-fill" :style="{ width: aiProgress + '%' }"></div>
            </div>
            <div v-if="aiProgressLabel" style="font-size: 0.8rem; color: var(--text-muted); text-align: center;">
              {{ aiProgressLabel }}
            </div>
          </div>
        </div>

        <div class="form-group" style="margin-top: 0.5rem;">
          <label for="ai-input">Unstructured Data Input</label>
          <textarea id="ai-input" v-model="aiInput" placeholder="Enter unstructured text..."></textarea>
        </div>

        <button class="btn" @click="runAiAgent" :disabled="!aiModelLoaded">Run AI Agent</button>
      </div>

      <div class="panel">
        <div class="panel-title">Extracted JSON Schema Output</div>
        <div class="output-container">
          <pre id="ai-output" class="output-pre" style="color: #60a5fa;">{{ aiOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Inference Time: <span id="ai-metric-time" class="metric-value">{{ aiTime }}</span></div>
          <div>Speed: <span id="ai-metric-speed" class="metric-value">{{ aiTokensPerSec }}</span></div>
        </div>
      </div>
    </div>
  </div>

  <!-- Custom Warning Modal Dialog -->
  <div v-if="showWarningModal" class="modal-overlay" @click.self="cancelDownload">
    <div class="modal-container">
      <div class="modal-header">
        <span class="modal-icon">⚠️</span>
        <span class="modal-title-text">Confirm Model Download</span>
      </div>
      <div class="modal-body">
        You are about to download the <strong>Gemma 4 (E2B) AI model (~1.1 GB)</strong> directly to your local browser storage.
        <br/><br/>
        This model runs completely locally on your device via <strong>WebGPU</strong> (with automatic CPU fallback), ensuring 100% data privacy. However, the download requires a stable internet connection and may take a few minutes.
      </div>
      <div class="modal-actions">
        <button class="btn btn-secondary" @click="cancelDownload">Cancel</button>
        <button class="btn" @click="startModelDownload">Download Gemma 4</button>
      </div>
    </div>
  </div>
</template>
