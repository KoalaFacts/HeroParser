import { ref } from "vue";
import {
  readCsv,
  writeCsv,
  detectCsvDelimiter,
  validateCsv,
  inferCsvSchema,
  repairLlmCsv,
  convertCsvToJsonl,
} from "heroparser";
import { downloadText } from "../utils/download";

const SAMPLE_CSV = [
  "Name,Age,Salary,Active,HireDate,ShiftStart,Role",
  "Alice,30,95000.50,true,2022-03-15,09:00:00,Developer",
  "Bob,25,78500.00,false,2024-07-01,09:30:00,Designer",
  'Charlie,34,,true,2020-11-20,08:45:00,"Tech, Lead"',
].join("\n");

const SAMPLE_LLM_DIRTY = '```csv\nName,Age,Role\nAlice,30,Developer\nBob,25,"Senior Designer\n';

export function useCsv() {
  const csvInput = ref(SAMPLE_CSV);
  const csvDelimiter = ref(",");
  const csvHasHeader = ref(true);
  const csvDistinguishTemporal = ref(true);
  const csvOutputTitle = ref("JSON Output");
  const csvOutput = ref('Click "Parse CSV" or choose an operation to see results...');
  const csvTime = ref("-");
  const csvCount = ref("-");
  const csvOutputKind = ref<"json" | "csv" | "jsonl" | "text">("text");

  const runCsvParse = () => {
    const t0 = performance.now();
    try {
      const result = readCsv(csvInput.value, {
        delimiter: csvDelimiter.value || ",",
        hasHeader: csvHasHeader.value,
      });
      const t1 = performance.now();
      csvOutputKind.value = "json";
      csvOutputTitle.value = "JSON Output";
      csvOutput.value = JSON.stringify(result, null, 2);
      csvTime.value = `${(t1 - t0).toFixed(2)} ms`;
      csvCount.value = result.length.toString();
    } catch (err: any) {
      csvOutputKind.value = "text";
      csvOutputTitle.value = "Parsing Error";
      csvOutput.value = `Parsing Error: ${err.message}`;
    }
  };

  const runCsvWrite = () => {
    const t0 = performance.now();
    try {
      const trimmed = csvInput.value.trimStart();
      const records = trimmed.startsWith("[")
        ? JSON.parse(csvInput.value)
        : readCsv(csvInput.value, {
            delimiter: csvDelimiter.value || ",",
            hasHeader: csvHasHeader.value,
          });
      const formatted = writeCsv(records, {
        delimiter: csvDelimiter.value || ",",
        hasHeader: csvHasHeader.value,
      });
      const t1 = performance.now();
      csvOutputKind.value = "csv";
      csvOutputTitle.value = "Written CSV Output";
      csvOutput.value = formatted;
      csvTime.value = `${(t1 - t0).toFixed(2)} ms`;
      csvCount.value = Array.isArray(records) ? records.length.toString() : "-";
    } catch (err: any) {
      csvOutputKind.value = "text";
      csvOutputTitle.value = "Write CSV Error";
      csvOutput.value = `Write CSV Error: ${err.message}`;
    }
  };

  const runCsvDelimiterDetect = () => {
    const t0 = performance.now();
    try {
      const delim = detectCsvDelimiter(csvInput.value);
      const t1 = performance.now();
      csvOutputKind.value = "text";
      csvOutputTitle.value = "Delimiter Detection";
      csvOutput.value = `Detected delimiter character: "${delim}"\n(Found in ${(t1 - t0).toFixed(2)} ms)`;
      csvTime.value = `${(t1 - t0).toFixed(2)} ms`;
      csvCount.value = "-";
    } catch (err: any) {
      csvOutputKind.value = "text";
      csvOutputTitle.value = "Detection Error";
      csvOutput.value = `Detection Error: ${err.message}`;
    }
  };

  const runCsvValidate = () => {
    const t0 = performance.now();
    try {
      const result = validateCsv(csvInput.value, {
        delimiter: csvDelimiter.value || undefined,
        hasHeader: csvHasHeader.value,
      });
      const t1 = performance.now();
      csvOutputKind.value = "json";
      csvOutputTitle.value = result.isValid ? "Validation Passed" : "Validation Issues";
      csvOutput.value = JSON.stringify(result, null, 2);
      csvTime.value = `${(t1 - t0).toFixed(2)} ms`;
      csvCount.value = result.totalRows.toString();
    } catch (err: any) {
      csvOutputKind.value = "text";
      csvOutputTitle.value = "Validation Error";
      csvOutput.value = `Validation Error: ${err.message}`;
    }
  };

  const runCsvInferSchema = () => {
    const t0 = performance.now();
    try {
      const result = inferCsvSchema(csvInput.value, {
        delimiter: csvDelimiter.value || undefined,
        distinguishTemporalTypes: csvDistinguishTemporal.value,
      });
      const t1 = performance.now();
      csvOutputKind.value = "json";
      csvOutputTitle.value = "Inferred Schema";
      csvOutput.value = JSON.stringify(result, null, 2);
      csvTime.value = `${(t1 - t0).toFixed(2)} ms`;
      csvCount.value = result.sampledRowCount.toString();
    } catch (err: any) {
      csvOutputKind.value = "text";
      csvOutputTitle.value = "Schema Inference Error";
      csvOutput.value = `Schema Inference Error: ${err.message}`;
    }
  };

  const runCsvRepairLlm = () => {
    const t0 = performance.now();
    try {
      const repaired = repairLlmCsv(csvInput.value);
      const t1 = performance.now();
      csvOutputKind.value = "csv";
      csvOutputTitle.value = "Repaired CSV";
      csvOutput.value = repaired;
      csvTime.value = `${(t1 - t0).toFixed(2)} ms`;
      csvCount.value = "-";
    } catch (err: any) {
      csvOutputKind.value = "text";
      csvOutputTitle.value = "Repair Error";
      csvOutput.value = `Repair Error: ${err.message}`;
    }
  };

  const runCsvToJsonl = () => {
    const t0 = performance.now();
    try {
      const jsonl = convertCsvToJsonl(csvInput.value, {
        delimiter: csvDelimiter.value || ",",
        hasHeader: csvHasHeader.value,
      });
      const t1 = performance.now();
      const trimmed = jsonl.trim();
      const lineCount = trimmed.length === 0 ? 0 : trimmed.split("\n").length;
      csvOutputKind.value = "jsonl";
      csvOutputTitle.value = "JSONL Output";
      csvOutput.value = jsonl;
      csvTime.value = `${(t1 - t0).toFixed(2)} ms`;
      csvCount.value = lineCount.toString();
    } catch (err: any) {
      csvOutputKind.value = "text";
      csvOutputTitle.value = "Conversion Error";
      csvOutput.value = `Conversion Error: ${err.message}`;
    }
  };

  const loadCsvSample = () => {
    csvInput.value = SAMPLE_CSV;
    csvDelimiter.value = ",";
    csvHasHeader.value = true;
  };

  const loadLlmSample = () => {
    csvInput.value = SAMPLE_LLM_DIRTY;
    csvDelimiter.value = ",";
    csvHasHeader.value = true;
  };

  const downloadCsvOutput = () => {
    if (csvOutputKind.value === "jsonl") {
      downloadText(
        csvOutput.value,
        "heroparser-output.jsonl",
        "application/x-ndjson;charset=utf-8",
      );
      return;
    }
    if (csvOutputKind.value === "csv") {
      downloadText(csvOutput.value, "heroparser-output.csv", "text/csv;charset=utf-8");
    }
  };

  return {
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
  };
}
