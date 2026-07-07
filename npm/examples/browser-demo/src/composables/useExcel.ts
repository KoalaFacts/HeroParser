import { ref } from 'vue'
import { readExcel } from 'heroparser'

export function useExcel() {
  const excelSheetName = ref("")
  const excelHasHeader = ref(true)
  const excelFileInfo = ref("")
  const excelError = ref("")
  const excelOutput = ref('Upload an Excel workbook and click "Parse Excel" to inspect contents...')
  const excelTime = ref('-')
  const excelCount = ref('-')
  let loadedExcelBytes: Uint8Array | null = null

  const handleExcelSelect = (e: any) => {
    const files = e.target.files
    if (files && files.length > 0) {
      loadExcelFile(files[0])
    }
  }

  const handleExcelDrop = (e: any) => {
    e.preventDefault()
    const files = e.dataTransfer.files
    if (files && files.length > 0 && files[0].name.endsWith('.xlsx')) {
      loadExcelFile(files[0])
    }
  }

  const loadExcelFile = (file: File) => {
    excelError.value = ""
    const reader = new FileReader()
    reader.onload = (e: any) => {
      loadedExcelBytes = new Uint8Array(e.target.result)
      excelFileInfo.value = `Loaded: ${file.name} (${(file.size / 1024).toFixed(1)} KB)`
    }
    reader.readAsArrayBuffer(file)
  }

  const runExcelParse = () => {
    if (!loadedExcelBytes) {
      excelError.value = "No Excel file loaded."
      return
    }

    excelError.value = ""
    const t0 = performance.now()
    try {
      const result = readExcel(loadedExcelBytes, excelSheetName.value || "", excelHasHeader.value)
      const t1 = performance.now()
      excelOutput.value = JSON.stringify(result, null, 2)
      excelTime.value = `${(t1 - t0).toFixed(2)} ms`
      excelCount.value = result.length.toString()
    } catch (err: any) {
      excelError.value = `Parsing Error: ${err.message}`
      excelOutput.value = "Error parsing file."
    }
  }

  return {
    excelSheetName,
    excelHasHeader,
    excelFileInfo,
    excelError,
    excelOutput,
    excelTime,
    excelCount,
    handleExcelSelect,
    handleExcelDrop,
    runExcelParse
  }
}
