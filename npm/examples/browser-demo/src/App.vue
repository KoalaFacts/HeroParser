<script setup vapor>
import { ref, onMounted } from 'vue'
import { init, parseCsv, detectCsvDelimiter, repairTabularOutput, parseFixedWidth, parseExcel } from 'heroparser'

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

// LLM Repair state
const repairInput = ref("Sure! Here is your CSV data:\n\n```csv\nName,Age,Role\nAlice,30,Developer\nBob,25,De")
const repairOutput = ref('Click "Repair Tabular Output" to run cleanup...')

onMounted(async () => {
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
    const result = parseCsv(csvInput.value, { 
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
    const result = parseFixedWidth(fwInput.value, specs)
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
    const result = parseExcel(loadedExcelBytes, excelSheetName.value || "", excelHasHeader.value)
    const t1 = performance.now()
    excelOutput.value = JSON.stringify(result, null, 2)
    excelTime.value = `${(t1 - t0).toFixed(2)} ms`
    excelCount.value = result.length.toString()
  } catch (err) {
    console.error(err)
    excelError.value = `WASM Parsing Error: ${err.message}`
  }
}

const runLlmRepair = () => {
  const t0 = performance.now()
  try {
    const repaired = repairTabularOutput(repairInput.value)
    const t1 = performance.now()
    repairOutput.value = repaired
  } catch (err) {
    repairOutput.value = `Repair Error: ${err.message}`
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
      <button :class="['tab-btn', { active: activeTab === 'repair' }]" @click="switchTab('repair')">LLM Repair</button>
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

    <!-- LLM Repair Content -->
    <div v-if="activeTab === 'repair'" id="tab-repair" class="tab-content active">
      <div class="panel">
        <div class="panel-title">LLM Output Input</div>
        <div class="form-group">
          <label for="repair-input">Cut-off or Conversational CSV</label>
          <textarea id="repair-input" v-model="repairInput" placeholder="CSV data..."></textarea>
        </div>
        <button class="btn" @click="runLlmRepair">Repair Tabular Output</button>
      </div>
      <div class="panel">
        <div class="panel-title">Clean CSV Output</div>
        <div class="output-container">
          <pre id="repair-output" class="output-pre">{{ repairOutput }}</pre>
        </div>
      </div>
    </div>
  </div>
</template>
