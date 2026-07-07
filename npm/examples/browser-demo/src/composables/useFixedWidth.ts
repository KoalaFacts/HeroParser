import { ref } from 'vue'
import { readFixedWidth } from 'heroparser'

export function useFixedWidth() {
  const fwInput = ref("Alice     30        Developer \nBob       25        Designer  ")
  const fwSpecs = ref(`[
  { "name": "Name", "start": 0, "length": 10 },
  { "name": "Age", "start": 10, "length": 10 },
  { "name": "Role", "start": 20, "length": 11 }
]`)
  const fwOutput = ref('Click "Parse Fixed-Width" to see results...')
  const fwTime = ref('-')
  const fwCount = ref('-')

  const runFixedWidthParse = () => {
    let specs
    try {
      specs = JSON.parse(fwSpecs.value)
    } catch (err: any) {
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
    } catch (err: any) {
      fwOutput.value = `Parsing Error: ${err.message}`
    }
  }

  return {
    fwInput,
    fwSpecs,
    fwOutput,
    fwTime,
    fwCount,
    runFixedWidthParse
  }
}
