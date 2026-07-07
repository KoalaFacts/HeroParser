<script setup vapor>
import { ref, onMounted } from 'vue'
import { init } from 'heroparser'
import { useCsv } from './composables/useCsv'
import { useFixedWidth } from './composables/useFixedWidth'
import { useExcel } from './composables/useExcel'
import { useAiCopilot } from './composables/useAiCopilot'

// Bootstrap state
const initialized = ref(false)
const initStatus = ref('Initializing HeroParser WASM Engine...')

// Tab selection
const activeTab = ref('csv')
const switchTab = (tab) => {
  activeTab.value = tab
}

// Load parsing states
const csv = useCsv()
const fw = useFixedWidth()
const excel = useExcel()
const ai = useAiCopilot()

const faviconUrl = './favicon.svg'
const iconsUrl = './icons.svg'

onMounted(async () => {
  // Check local cache settings on startup
  ai.checkCacheOnMount()
  
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
</script>

<template>
  <!-- Loading Overlay -->
  <div v-if="!initialized" id="init-overlay">
    <div class="spinner"></div>
    <div id="init-status">{{ initStatus }}</div>
  </div>

  <div class="container">
    <!-- Header link bar -->
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

    <!-- Header title -->
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
          <textarea id="csv-input" v-model="csv.csvInput" placeholder="Name,Age,Role"></textarea>
        </div>
        <div class="options-grid">
          <div class="form-group">
            <label for="csv-delimiter">Delimiter</label>
            <input type="text" id="csv-delimiter" v-model="csv.csvDelimiter" maxlength="1">
          </div>
          <div class="flex-center-gap margin-top-large">
            <input type="checkbox" id="csv-header" v-model="csv.csvHasHeader" class="checkbox-custom">
            <label for="csv-header" class="cursor-pointer">Has Header Row</label>
          </div>
        </div>
        <div class="options-grid">
          <button class="btn" @click="csv.runCsvParse">Parse CSV</button>
          <button class="btn btn-secondary" @click="csv.runCsvDelimiterDetect">Detect Delimiter</button>
        </div>
      </div>
      <div class="panel">
        <div class="panel-title">JSON Output</div>
        <div class="output-container">
          <pre id="csv-output" class="output-pre">{{ csv.csvOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Parse Time: <span id="csv-metric-time" class="metric-value">{{ csv.csvTime }}</span></div>
          <div>Records: <span id="csv-metric-count" class="metric-value">{{ csv.csvCount }}</span></div>
        </div>
      </div>
    </div>

    <!-- Fixed-Width Content -->
    <div v-if="activeTab === 'fixedwidth'" id="tab-fixedwidth" class="tab-content active">
      <div class="panel">
        <div class="panel-title">Fixed-Width Input</div>
        <div class="form-group">
          <label for="fw-input">Fixed-Width Text Data</label>
          <textarea id="fw-input" v-model="fw.fwInput" placeholder="Alice     30        Developer"></textarea>
        </div>
        <div class="form-group">
          <label for="fw-specs">Column Ranges (JSON Specification)</label>
          <textarea id="fw-specs" v-model="fw.fwSpecs" style="min-height: 120px;"></textarea>
        </div>
        <button class="btn" @click="fw.runFixedWidthParse">Parse Fixed-Width</button>
      </div>
      <div class="panel">
        <div class="panel-title">JSON Output</div>
        <div class="output-container">
          <pre id="fw-output" class="output-pre">{{ fw.fwOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Parse Time: <span id="fw-metric-time" class="metric-value">{{ fw.fwTime }}</span></div>
          <div>Records: <span id="fw-metric-count" class="metric-value">{{ fw.fwCount }}</span></div>
        </div>
      </div>
    </div>

    <!-- Excel Content -->
    <div v-if="activeTab === 'excel'" id="tab-excel" class="tab-content active">
      <div class="panel">
        <div class="panel-title">Excel (.xlsx) Upload</div>
        <div id="excel-dropzone" class="dropzone" @click="document.getElementById('excel-file').click()" @dragover.prevent @drop.prevent="excel.handleExcelDrop">
          <span class="dropzone-icon">📥</span>
          <span class="dropzone-text">Drag and drop an Excel (.xlsx) file here, or click to browse</span>
          <span v-if="excel.excelFileInfo" id="excel-file-info" class="dropzone-file-info" style="display: block;">{{ excel.excelFileInfo }}</span>
          <input type="file" id="excel-file" accept=".xlsx" style="display: none;" @change="excel.handleExcelSelect">
        </div>
        <div class="options-grid">
          <div class="form-group">
            <label for="excel-sheet">Sheet Name (leave empty for first sheet)</label>
            <input type="text" id="excel-sheet" v-model="excel.excelSheetName" placeholder="Sheet1">
          </div>
          <div class="flex-center-gap margin-top-large">
            <input type="checkbox" id="excel-header" v-model="excel.excelHasHeader" class="checkbox-custom">
            <label for="excel-header" class="cursor-pointer">Has Header Row</label>
          </div>
        </div>
        <button class="btn" id="btn-parse-excel" @click="excel.runExcelParse" :disabled="!excel.excelFileInfo">Parse Excel Sheet</button>
        <div v-if="excel.excelError" id="excel-error" class="alert-error" style="display: block;">{{ excel.excelError }}</div>
      </div>
      <div class="panel">
        <div class="panel-title">JSON Output</div>
        <div class="output-container">
          <pre id="excel-output" class="output-pre">{{ excel.excelOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Parse Time: <span id="excel-metric-time" class="metric-value">{{ excel.excelTime }}</span></div>
          <div>Records: <span id="excel-metric-count" class="metric-value">{{ excel.excelCount }}</span></div>
        </div>
      </div>
    </div>

    <!-- AI Copilot Content (WebGPU Lazy-loaded) -->
    <div v-if="activeTab === 'ai'" id="tab-ai" class="tab-content active">
      <div class="panel">
        <div class="panel-title">Local AI Configuration (WebGPU)</div>
        
        <div class="ai-card">
          <div class="ai-header-bar">
            <span style="font-weight: 600; font-size: 0.95rem;">Gemma 4 (E2B) AI Model</span>
            <span class="ai-meta-size">~1.1 GB (Quantized)</span>
          </div>

          <div class="flex-column-gap">
            <div class="flex-between">
              <span class="status-badge" :class="ai.aiModelLoaded ? 'ready' : (ai.aiLoading ? 'loading' : 'not-loaded')">
                <span v-if="ai.aiModelLoaded">🟢 Loaded & Ready</span>
                <span v-else-if="ai.aiLoading">🟡 Downloading...</span>
                <span v-else>⚪ Not Loaded</span>
              </span>
              <button v-if="!ai.aiModelLoaded" class="btn btn-secondary btn-small" @click="ai.triggerDownloadWarning" :disabled="ai.aiLoading">
                Load AI Model
              </button>
              <button v-else class="btn btn-secondary btn-small btn-danger-outline" @click="ai.clearAiCache">
                Clear Cache
              </button>
            </div>
            
            <div v-if="ai.aiLoading || ai.aiModelLoaded" class="progress-container">
              <div class="progress-bar-fill" :style="{ width: ai.aiProgress + '%' }"></div>
            </div>
            <div v-if="ai.aiProgressLabel" class="ai-progress-text">
              {{ ai.aiProgressLabel }}
            </div>
          </div>
        </div>

        <div class="form-group margin-top-small">
          <label for="ai-input">Unstructured Data Input</label>
          <textarea id="ai-input" v-model="ai.aiInput" placeholder="Enter unstructured text..."></textarea>
        </div>

        <button class="btn" @click="ai.runAiAgent" :disabled="!ai.aiModelLoaded">Run AI Agent</button>
      </div>

      <div class="panel">
        <div class="panel-title">Extracted JSON Schema Output</div>
        <div class="output-container">
          <pre id="ai-output" class="output-pre output-blue">{{ ai.aiOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Inference Time: <span id="ai-metric-time" class="metric-value">{{ ai.aiTime }}</span></div>
          <div>Speed: <span id="ai-metric-speed" class="metric-value">{{ ai.aiTokensPerSec }}</span></div>
        </div>
      </div>
    </div>
  </div>

  <!-- Custom Warning Modal Dialog -->
  <div v-if="ai.showWarningModal" class="modal-overlay" @click.self="ai.cancelDownload">
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
        <button class="btn btn-secondary" @click="ai.cancelDownload">Cancel</button>
        <button class="btn" @click="ai.startModelDownload">Download Gemma 4</button>
      </div>
    </div>
  </div>
</template>
