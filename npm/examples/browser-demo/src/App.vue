<script setup vapor lang="ts">
import { ref, onMounted } from "vue";
import { init } from "heroparser";
import { useCsv } from "./composables/useCsv";
import { useFixedWidth } from "./composables/useFixedWidth";
import { useExcel } from "./composables/useExcel";
import { useJsonl } from "./composables/useJsonl";
import { useHtb } from "./composables/useHtb";
import { useAiCopilot } from "./composables/useAiCopilot";
import heroParserPackage from "heroparser/package.json";

// Bootstrap state
const initialized = ref(false);
const initStatus = ref("Initializing HeroParser WASM Engine...");

// Tab selection
const activeTab = ref("csv");
const switchTab = (tab: string) => {
  activeTab.value = tab;
};

// Load parsing states
const {
  csvInput,
  csvDelimiter,
  csvHasHeader,
  csvDistinguishTemporal,
  csvOutputTitle,
  csvOutput,
  csvTime,
  csvCount,
  csvOutputKind,
  runCsvParse,
  runCsvWrite,
  runCsvDelimiterDetect,
  runCsvValidate,
  runCsvInferSchema,
  runCsvRepairLlm,
  runCsvToJsonl,
  loadCsvSample,
  loadLlmSample,
  downloadCsvOutput,
} = useCsv();

const {
  fwInput,
  fwSpecs,
  fwDelimiter,
  fwIncludeHeader,
  fwOutputTitle,
  fwOutput,
  fwTime,
  fwCount,
  fwOutputKind,
  runFixedWidthParse,
  runFixedWidthWrite,
  runFixedWidthToCsv,
  runCsvToFixedWidth,
  loadFixedWidthSample,
  loadFixedWidthJsonSample,
  loadFixedWidthCsvSample,
  downloadFixedWidthOutput,
} = useFixedWidth();

const {
  excelSheetName,
  excelSheetNames,
  excelHasHeader,
  excelJsonInput,
  excelFileInfo,
  excelError,
  excelOutputTitle,
  excelOutput,
  excelTime,
  excelCount,
  excelOutputKind,
  handleExcelSelect,
  handleExcelDrop,
  runExcelParse,
  selectExcelSheet,
  buildExcelFromJson,
  loadSampleExcel,
  loadMultiSheetSample,
  exportExcelToCsv,
  downloadSampleExcel,
  downloadExcelCsv,
} = useExcel();

const {
  jsonlInput,
  jsonlInputFormat,
  jsonlDelimiter,
  jsonlOutputTitle,
  jsonlOutput,
  jsonlTime,
  jsonlCount,
  jsonlOutputKind,
  runJsonlParse,
  runJsonlToCsv,
  runJsonlWrite,
  loadJsonlSample,
  loadJsonArrayPreset,
  downloadJsonlOutput,
} = useJsonl();

const {
  htbCsvInput,
  htbDelimiter,
  htbHasHeader,
  htbDistinguishTemporal,
  htbFileInfo,
  htbError,
  htbOutputTitle,
  htbOutput,
  htbTime,
  htbCount,
  htbSizeSummary,
  hasHtbBytes,
  htbOutputKind,
  runCsvToHtb,
  runHtbToCsv,
  runHtbInspect,
  handleHtbSelect,
  handleHtbDrop,
  loadHtbSample,
  downloadHtb,
  downloadHtbCsv,
} = useHtb();

const {
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
  checkCacheOnMount,
} = useAiCopilot();

const heroParserVersion = heroParserPackage.version;
const excelFileInput = ref<HTMLInputElement | null>(null);
const openExcelPicker = () => excelFileInput.value?.click();
const htbFileInput = ref<HTMLInputElement | null>(null);
const openHtbPicker = () => htbFileInput.value?.click();

const faviconUrl = "./favicon.svg";
const iconsUrl = "./icons.svg";

onMounted(async () => {
  // Check local cache settings on startup
  checkCacheOnMount();

  try {
    console.log("Bootstrapping WASM inside Vue Vapor SFC...");
    await init();
    console.log("WASM Initialized successfully!");
    initialized.value = true;
  } catch (err) {
    console.error("Initialization failed:", err);
    initStatus.value = "Failed to load WASM engine. Check dev console logs.";
  }
});
</script>

<template>
  <!-- Loading Overlay -->
  <div v-if="!initialized" id="init-overlay">
    <div class="spinner"></div>
    <div id="init-status">{{ initStatus }}</div>
  </div>

  <div class="container">
    <!-- Header link bar -->
    <div
      style="
        display: flex;
        justify-content: flex-end;
        gap: 1.5rem;
        margin-bottom: 1.5rem;
        font-size: 0.95rem;
      "
    >
      <a
        href="https://github.com/KoalaFacts/HeroParser"
        target="_blank"
        style="
          display: flex;
          align-items: center;
          gap: 0.4rem;
          color: var(--text-muted);
          text-decoration: none;
          transition: color 0.2s;
        "
        onmouseover="this.style.color = 'var(--text)'"
        onmouseout="this.style.color = 'var(--text-muted)'"
      >
        <svg style="width: 18px; height: 18px; fill: currentColor">
          <use :href="`${iconsUrl}#github-icon`"></use>
        </svg>
        GitHub
      </a>
      <a
        href="https://github.com/KoalaFacts/HeroParser#readme"
        target="_blank"
        style="
          display: flex;
          align-items: center;
          gap: 0.4rem;
          color: var(--text-muted);
          text-decoration: none;
          transition: color 0.2s;
        "
        onmouseover="this.style.color = 'var(--text)'"
        onmouseout="this.style.color = 'var(--text-muted)'"
      >
        <svg style="width: 18px; height: 18px; fill: none; stroke: currentColor">
          <use :href="`${iconsUrl}#documentation-icon`"></use>
        </svg>
        Documentation
      </a>
    </div>

    <!-- Header title -->
    <header>
      <div
        style="
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 1rem;
          margin-bottom: 0.5rem;
        "
      >
        <img :src="faviconUrl" alt="HeroParser Logo" style="width: 48px; height: 48px" />
        <h1 style="margin: 0">HeroParser WASM</h1>
        <span class="ai-meta-size" title="HeroParser package version"
          >v{{ heroParserVersion }}</span
        >
      </div>
      <p class="tagline">
        HeroParser's low-allocation C# engine for CSV, Fixed-Width, Excel (.xlsx), JSONL, and HTB
        binary formats — with schema analysis, LLM repair, and format converters compiled to
        WebAssembly in your browser. Timings shown here are single in-browser runs, not benchmarks.
      </p>
    </header>

    <!-- Tabs Nav -->
    <div class="tabs-nav">
      <button
        id="nav-tab-csv"
        :class="['tab-btn', { active: activeTab === 'csv' }]"
        @click="switchTab('csv')"
      >
        CSV Parser
      </button>
      <button
        id="nav-tab-fixedwidth"
        :class="['tab-btn', { active: activeTab === 'fixedwidth' }]"
        @click="switchTab('fixedwidth')"
      >
        Fixed-Width
      </button>
      <button
        id="nav-tab-excel"
        :class="['tab-btn', { active: activeTab === 'excel' }]"
        @click="switchTab('excel')"
      >
        Excel (.xlsx)
      </button>
      <button
        id="nav-tab-jsonl"
        :class="['tab-btn', { active: activeTab === 'jsonl' }]"
        @click="switchTab('jsonl')"
      >
        JSONL
      </button>
      <button
        id="nav-tab-htb"
        :class="['tab-btn', { active: activeTab === 'htb' }]"
        @click="switchTab('htb')"
      >
        HTB Binary
      </button>
      <button
        id="nav-tab-ai"
        :class="['tab-btn', { active: activeTab === 'ai' }]"
        @click="switchTab('ai')"
      >
        AI Copilot (WebGPU)
      </button>
    </div>

    <!-- CSV Content -->
    <div v-if="activeTab === 'csv'" id="tab-csv" class="tab-content active">
      <div class="panel">
        <div class="panel-title">CSV Input</div>
        <div class="form-group">
          <div class="preset-bar">
            <label for="csv-input">CSV Text or JSON Array Input</label>
            <div class="preset-actions">
              <button class="btn-ghost" type="button" @click="loadCsvSample">
                Load Sample CSV
              </button>
              <button class="btn-ghost" type="button" @click="loadLlmSample">
                Load Messy LLM CSV
              </button>
            </div>
          </div>
          <textarea id="csv-input" v-model="csvInput" placeholder="Name,Age,Role"></textarea>
        </div>
        <div class="options-grid">
          <div class="form-group">
            <label for="csv-delimiter">Delimiter</label>
            <input type="text" id="csv-delimiter" v-model="csvDelimiter" maxlength="1" />
          </div>
          <div class="flex-column-gap" style="justify-content: flex-end">
            <div class="flex-center-gap">
              <input
                type="checkbox"
                id="csv-header"
                v-model="csvHasHeader"
                class="checkbox-custom"
              />
              <label for="csv-header" class="cursor-pointer">Has Header Row</label>
            </div>
            <div class="flex-center-gap">
              <input
                type="checkbox"
                id="csv-temporal"
                v-model="csvDistinguishTemporal"
                class="checkbox-custom"
              />
              <label for="csv-temporal" class="cursor-pointer">Distinguish Temporal Types</label>
            </div>
          </div>
        </div>
        <div class="options-grid">
          <button id="btn-csv-parse" class="btn" @click="runCsvParse">Parse CSV</button>
          <button id="btn-csv-write" class="btn btn-secondary" @click="runCsvWrite">
            Write / Format CSV
          </button>
          <button id="btn-csv-detect" class="btn btn-secondary" @click="runCsvDelimiterDetect">
            Detect Delimiter
          </button>
          <button id="btn-csv-validate" class="btn btn-secondary" @click="runCsvValidate">
            Validate CSV
          </button>
          <button id="btn-csv-schema" class="btn btn-secondary" @click="runCsvInferSchema">
            Infer Schema
          </button>
          <button id="btn-csv-repair" class="btn btn-secondary" @click="runCsvRepairLlm">
            Repair LLM CSV
          </button>
          <button id="btn-csv-to-jsonl" class="btn btn-secondary" @click="runCsvToJsonl">
            CSV &rarr; JSONL
          </button>
        </div>
      </div>
      <div class="panel">
        <div class="panel-header-row">
          <div class="panel-title">{{ csvOutputTitle }}</div>
          <button
            v-if="csvOutputKind === 'csv' || csvOutputKind === 'jsonl'"
            id="btn-csv-download"
            class="btn btn-secondary btn-small"
            @click="downloadCsvOutput"
          >
            Download {{ csvOutputKind === "jsonl" ? ".jsonl" : ".csv" }}
          </button>
        </div>
        <div class="output-container">
          <pre id="csv-output" class="output-pre">{{ csvOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Elapsed: <span id="csv-metric-time" class="metric-value">{{ csvTime }}</span></div>
          <div>Records: <span id="csv-metric-count" class="metric-value">{{ csvCount }}</span></div>
        </div>
      </div>
    </div>

    <!-- Fixed-Width Content -->
    <div v-if="activeTab === 'fixedwidth'" id="tab-fixedwidth" class="tab-content active">
      <div class="panel">
        <div class="panel-title">Fixed-Width Input &amp; Converter</div>
        <div class="form-group">
          <div class="preset-bar">
            <label for="fw-input">Fixed-Width, CSV, or JSON Input</label>
            <div class="preset-actions">
              <button class="btn-ghost" type="button" @click="loadFixedWidthSample">
                Fixed-Width Sample
              </button>
              <button class="btn-ghost" type="button" @click="loadFixedWidthJsonSample">
                JSON Sample
              </button>
              <button class="btn-ghost" type="button" @click="loadFixedWidthCsvSample">
                CSV Sample
              </button>
            </div>
          </div>
          <textarea
            id="fw-input"
            v-model="fwInput"
            placeholder="Alice     00030  95000.50Developer"
          ></textarea>
        </div>
        <div class="form-group">
          <label for="fw-specs">Column Ranges (JSON Spec: start, length, alignment, padChar)</label>
          <textarea id="fw-specs" v-model="fwSpecs" style="min-height: 130px"></textarea>
        </div>
        <div class="options-grid">
          <div class="form-group">
            <label for="fw-delimiter">CSV Delimiter (for Conversion)</label>
            <input type="text" id="fw-delimiter" v-model="fwDelimiter" maxlength="1" />
          </div>
          <div class="flex-center-gap margin-top-large">
            <input
              type="checkbox"
              id="fw-include-header"
              v-model="fwIncludeHeader"
              class="checkbox-custom"
            />
            <label for="fw-include-header" class="cursor-pointer">Include Header on Convert</label>
          </div>
        </div>
        <div class="options-grid">
          <button id="btn-fw-parse" class="btn" @click="runFixedWidthParse">
            Parse Fixed-Width
          </button>
          <button id="btn-fw-write" class="btn btn-secondary" @click="runFixedWidthWrite">
            Write Fixed-Width
          </button>
          <button id="btn-fw-to-csv" class="btn btn-secondary" @click="runFixedWidthToCsv">
            Fixed-Width &rarr; CSV
          </button>
          <button id="btn-csv-to-fw" class="btn btn-secondary" @click="runCsvToFixedWidth">
            CSV &rarr; Fixed-Width
          </button>
        </div>
      </div>
      <div class="panel">
        <div class="panel-header-row">
          <div class="panel-title">{{ fwOutputTitle }}</div>
          <button
            v-if="fwOutputKind === 'text' || fwOutputKind === 'csv'"
            id="btn-fw-download"
            class="btn btn-secondary btn-small"
            @click="downloadFixedWidthOutput"
          >
            Download {{ fwOutputKind === "csv" ? ".csv" : ".txt" }}
          </button>
        </div>
        <div class="output-container">
          <pre
            id="fw-output"
            :class="[
              'output-pre',
              { 'output-pre-nowrap': fwOutputKind === 'text' || fwOutputKind === 'csv' },
            ]"
            >{{ fwOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Elapsed: <span id="fw-metric-time" class="metric-value">{{ fwTime }}</span></div>
          <div>Records: <span id="fw-metric-count" class="metric-value">{{ fwCount }}</span></div>
        </div>
      </div>
    </div>

    <!-- Excel Content -->
    <div v-if="activeTab === 'excel'" id="tab-excel" class="tab-content active">
      <div class="panel">
        <div class="panel-title">Excel (.xlsx) Read, Write &amp; Multi-Sheet</div>
        <div
          id="excel-dropzone"
          class="dropzone dropzone-compact"
          @click="openExcelPicker"
          @dragover.prevent
          @drop.prevent="handleExcelDrop"
        >
          <span class="dropzone-icon">📥</span>
          <span class="dropzone-text"
            >Drag and drop an Excel (.xlsx) file here, or click to browse</span
          >
          <span
            v-if="excelFileInfo"
            id="excel-file-info"
            class="dropzone-file-info"
            style="display: block"
            >{{ excelFileInfo }}</span
          >
          <input
            ref="excelFileInput"
            type="file"
            id="excel-file"
            accept=".xlsx"
            style="display: none"
            @change="handleExcelSelect"
          />
        </div>

        <div v-if="excelSheetNames.length > 0" class="form-group">
          <label>Detected Sheets (click to inspect)</label>
          <div class="sheet-pills">
            <button
              v-for="name in excelSheetNames"
              :key="name"
              type="button"
              :class="['sheet-pill', { active: excelSheetName === name }]"
              @click="selectExcelSheet(name)"
            >
              {{ name }}
            </button>
          </div>
        </div>

        <div class="options-grid">
          <div class="form-group">
            <label for="excel-sheet">Sheet Name (empty = first sheet)</label>
            <input type="text" id="excel-sheet" v-model="excelSheetName" placeholder="Sheet1" />
          </div>
          <div class="flex-center-gap margin-top-large">
            <input
              type="checkbox"
              id="excel-header"
              v-model="excelHasHeader"
              class="checkbox-custom"
            />
            <label for="excel-header" class="cursor-pointer">Has Header Row</label>
          </div>
        </div>

        <div class="form-group">
          <label for="excel-json-input">Editable JSON Records (for Building Custom .xlsx)</label>
          <textarea
            id="excel-json-input"
            v-model="excelJsonInput"
            style="min-height: 120px"
          ></textarea>
        </div>

        <div class="options-grid">
          <button
            class="btn"
            id="btn-parse-excel"
            @click="runExcelParse"
            :disabled="!excelFileInfo"
          >
            Parse Excel Sheet
          </button>
          <button class="btn btn-secondary" id="btn-excel-build" @click="buildExcelFromJson">
            Build .xlsx from JSON
          </button>
          <button class="btn btn-secondary" id="btn-sample-excel" @click="loadSampleExcel">
            Generate Sample (.xlsx)
          </button>
          <button class="btn btn-secondary" id="btn-excel-multisheet" @click="loadMultiSheetSample">
            Generate Multi-Sheet
          </button>
          <button
            class="btn btn-secondary"
            id="btn-excel-to-csv"
            @click="exportExcelToCsv"
            :disabled="!excelFileInfo"
          >
            Excel &rarr; CSV
          </button>
          <button
            class="btn btn-secondary"
            id="btn-download-excel"
            @click="downloadSampleExcel"
            :disabled="!excelFileInfo"
          >
            Download .xlsx
          </button>
        </div>
        <div v-if="excelError" id="excel-error" class="alert-error" style="display: block">
          {{ excelError }}
        </div>
      </div>
      <div class="panel">
        <div class="panel-header-row">
          <div class="panel-title">{{ excelOutputTitle }}</div>
          <button
            v-if="excelOutputKind === 'csv'"
            id="btn-excel-csv-download"
            class="btn btn-secondary btn-small"
            @click="downloadExcelCsv"
          >
            Download .csv
          </button>
        </div>
        <div class="output-container">
          <pre id="excel-output" class="output-pre">{{ excelOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>
            Elapsed: <span id="excel-metric-time" class="metric-value">{{ excelTime }}</span>
          </div>
          <div>
            Records: <span id="excel-metric-count" class="metric-value">{{ excelCount }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- JSONL Content -->
    <div v-if="activeTab === 'jsonl'" id="tab-jsonl" class="tab-content active">
      <div class="panel">
        <div class="panel-title">JSONL (JSON Lines) Input &amp; Converter</div>
        <div class="form-group">
          <div class="preset-bar">
            <label for="jsonl-input">JSONL Text or JSON Array Input</label>
            <div class="preset-actions">
              <button class="btn-ghost" type="button" @click="loadJsonlSample">JSONL Sample</button>
              <button class="btn-ghost" type="button" @click="loadJsonArrayPreset">
                JSON Array Preset
              </button>
            </div>
          </div>
          <textarea
            id="jsonl-input"
            v-model="jsonlInput"
            placeholder='{"id":1,"name":"Alice"}'
          ></textarea>
        </div>
        <div class="options-grid">
          <div class="form-group">
            <label for="jsonl-input-format">Write Input Format</label>
            <select id="jsonl-input-format" v-model="jsonlInputFormat">
              <option value="jsonl">JSONL Lines</option>
              <option value="json-array">JSON Array</option>
            </select>
          </div>
          <div class="form-group">
            <label for="jsonl-delimiter">CSV Delimiter (for JSONL &rarr; CSV)</label>
            <input type="text" id="jsonl-delimiter" v-model="jsonlDelimiter" maxlength="1" />
          </div>
        </div>
        <div class="options-grid">
          <button id="btn-jsonl-parse" class="btn" @click="runJsonlParse">Parse JSONL</button>
          <button id="btn-jsonl-to-csv" class="btn btn-secondary" @click="runJsonlToCsv">
            JSONL &rarr; CSV
          </button>
          <button id="btn-jsonl-write" class="btn btn-secondary" @click="runJsonlWrite">
            Write JSONL
          </button>
        </div>
      </div>
      <div class="panel">
        <div class="panel-header-row">
          <div class="panel-title">{{ jsonlOutputTitle }}</div>
          <button
            v-if="jsonlOutputKind === 'csv' || jsonlOutputKind === 'jsonl'"
            id="btn-jsonl-download"
            class="btn btn-secondary btn-small"
            @click="downloadJsonlOutput"
          >
            Download {{ jsonlOutputKind === "csv" ? ".csv" : ".jsonl" }}
          </button>
        </div>
        <div class="output-container">
          <pre id="jsonl-output" class="output-pre">{{ jsonlOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>
            Elapsed: <span id="jsonl-metric-time" class="metric-value">{{ jsonlTime }}</span>
          </div>
          <div>
            Records: <span id="jsonl-metric-count" class="metric-value">{{ jsonlCount }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- HTB Binary Content -->
    <div v-if="activeTab === 'htb'" id="tab-htb" class="tab-content active">
      <div class="panel">
        <div class="panel-title">HTB Binary Format (High-Throughput Tabular Binary)</div>
        <div class="form-group">
          <div class="preset-bar">
            <label for="htb-csv-input">Source CSV (Encoded Using Inferred Schema)</label>
            <div class="preset-actions">
              <button class="btn-ghost" type="button" @click="loadHtbSample">Reset Sample</button>
            </div>
          </div>
          <textarea id="htb-csv-input" v-model="htbCsvInput" style="min-height: 140px"></textarea>
        </div>

        <div class="options-grid">
          <div class="form-group">
            <label for="htb-delimiter">CSV Delimiter</label>
            <input type="text" id="htb-delimiter" v-model="htbDelimiter" maxlength="1" />
          </div>
          <div class="flex-column-gap" style="justify-content: flex-end">
            <div class="flex-center-gap">
              <input
                type="checkbox"
                id="htb-header"
                v-model="htbHasHeader"
                class="checkbox-custom"
              />
              <label for="htb-header" class="cursor-pointer">Has Header Row</label>
            </div>
            <div class="flex-center-gap">
              <input
                type="checkbox"
                id="htb-temporal"
                v-model="htbDistinguishTemporal"
                class="checkbox-custom"
              />
              <label for="htb-temporal" class="cursor-pointer">Distinguish Temporal Types</label>
            </div>
          </div>
        </div>

        <div
          id="htb-dropzone"
          class="dropzone dropzone-compact"
          @click="openHtbPicker"
          @dragover.prevent
          @drop.prevent="handleHtbDrop"
        >
          <span class="dropzone-icon">📦</span>
          <span class="dropzone-text"
            >Or upload an existing .htb binary file to inspect/decode</span
          >
          <span
            v-if="htbFileInfo"
            id="htb-file-info"
            class="dropzone-file-info"
            style="display: block"
            >{{ htbFileInfo }}</span
          >
          <input
            ref="htbFileInput"
            type="file"
            id="htb-file"
            accept=".htb"
            style="display: none"
            @change="handleHtbSelect"
          />
        </div>

        <div class="options-grid">
          <button id="btn-htb-encode" class="btn" @click="runCsvToHtb">
            Encode CSV &rarr; HTB
          </button>
          <button
            id="btn-htb-to-csv"
            class="btn btn-secondary"
            @click="runHtbToCsv"
            :disabled="!hasHtbBytes"
          >
            Decode HTB &rarr; CSV
          </button>
          <button
            id="btn-htb-inspect"
            class="btn btn-secondary"
            @click="runHtbInspect"
            :disabled="!hasHtbBytes"
          >
            Inspect HTB Schema
          </button>
          <button
            id="btn-htb-download"
            class="btn btn-secondary"
            @click="downloadHtb"
            :disabled="!hasHtbBytes"
          >
            Download .htb
          </button>
        </div>

        <div v-if="htbError" id="htb-error" class="alert-error" style="display: block">
          {{ htbError }}
        </div>
      </div>
      <div class="panel">
        <div class="panel-header-row">
          <div class="panel-title">{{ htbOutputTitle }}</div>
          <button
            v-if="htbOutputKind === 'csv'"
            id="btn-htb-csv-download"
            class="btn btn-secondary btn-small"
            @click="downloadHtbCsv"
          >
            Download .csv
          </button>
        </div>
        <div class="output-container">
          <pre id="htb-output" class="output-pre">{{ htbOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>Elapsed: <span id="htb-metric-time" class="metric-value">{{ htbTime }}</span></div>
          <div>Records: <span id="htb-metric-count" class="metric-value">{{ htbCount }}</span></div>
          <div>
            Payload: <span id="htb-metric-size" class="metric-value">{{ htbSizeSummary }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- AI Copilot Content (WebGPU Lazy-loaded) -->
    <div v-if="activeTab === 'ai'" id="tab-ai" class="tab-content active">
      <div class="panel">
        <div class="panel-title">Local AI Configuration (WebGPU)</div>

        <div class="ai-card">
          <div class="ai-header-bar">
            <span style="font-weight: 600; font-size: 0.95rem">Gemma 4 (E2B) AI Model</span>
            <span class="ai-meta-size">~1.1 GB (Quantized)</span>
          </div>

          <div class="flex-column-gap">
            <div class="flex-between">
              <span
                class="status-badge"
                :class="aiModelLoaded ? 'ready' : aiLoading ? 'loading' : 'not-loaded'"
              >
                <span v-if="aiModelLoaded">🟢 Loaded & Ready</span>
                <span v-else-if="aiLoading">🟡 Downloading...</span>
                <span v-else>⚪ Not Loaded</span>
              </span>
              <button
                v-if="!aiModelLoaded"
                class="btn btn-secondary btn-small"
                @click="triggerDownloadWarning"
                :disabled="aiLoading"
              >
                Load AI Model
              </button>
              <button
                v-else
                class="btn btn-secondary btn-small btn-danger-outline"
                @click="clearAiCache"
              >
                Clear Cache
              </button>
            </div>

            <div v-if="aiLoading || aiModelLoaded" class="progress-container">
              <div class="progress-bar-fill" :style="{ width: aiProgress + '%' }"></div>
            </div>
            <div v-if="aiProgressLabel" class="ai-progress-text">
              {{ aiProgressLabel }}
            </div>
          </div>
        </div>

        <div class="form-group margin-top-small">
          <label for="ai-input">Unstructured Data Input</label>
          <textarea
            id="ai-input"
            v-model="aiInput"
            placeholder="Enter unstructured text..."
          ></textarea>
        </div>

        <button
          class="btn"
          @click="runAiAgent"
          :disabled="!aiModelLoaded || aiLoading || aiInferenceActive"
        >
          <span v-if="aiInferenceActive">Generating...</span>
          <span v-else-if="aiLoading">Loading...</span>
          <span v-else>Run AI Agent</span>
        </button>
      </div>

      <div class="panel">
        <div class="panel-title">Extracted JSON Schema Output</div>
        <div class="output-container">
          <pre id="ai-output" class="output-pre output-blue">{{ aiOutput }}</pre>
        </div>
        <div class="metrics-bar">
          <div>
            Inference Time: <span id="ai-metric-time" class="metric-value">{{ aiTime }}</span>
          </div>
          <div>
            Speed: <span id="ai-metric-speed" class="metric-value">{{ aiTokensPerSec }}</span>
          </div>
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
        You are about to download the <strong>Gemma 4 (E2B) AI model (~1.1 GB)</strong> directly to
        your local browser storage. <br /><br />
        This model runs completely locally on your device via <strong>WebGPU</strong> (with
        automatic CPU fallback), ensuring 100% data privacy. However, the download requires a stable
        internet connection and may take a few minutes.
      </div>
      <div class="modal-actions">
        <button class="btn btn-secondary" @click="cancelDownload">Cancel</button>
        <button class="btn" @click="startModelDownload">Download Gemma 4</button>
      </div>
    </div>
  </div>
</template>
