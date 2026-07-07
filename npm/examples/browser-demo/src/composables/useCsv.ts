import { ref } from 'vue'
import { readCsv, detectCsvDelimiter } from 'heroparser'

export function useCsv() {
  const csvInput = ref("Name,Age,Role\nAlice,30,Developer\nBob,25,Designer")
  const csvDelimiter = ref(",")
  const csvHasHeader = ref(true)
  const csvOutput = ref('Click "Parse CSV" to see results...')
  const csvTime = ref('-')
  const csvCount = ref('-')

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
    } catch (err: any) {
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
    } catch (err: any) {
      csvOutput.value = `Detection Error: ${err.message}`
    }
  }

  return {
    csvInput,
    csvDelimiter,
    csvHasHeader,
    csvOutput,
    csvTime,
    csvCount,
    runCsvParse,
    runCsvDelimiterDetect
  }
}
