import { ref } from "vue";
import {
  readFixedWidth,
  writeFixedWidth,
  convertCsvToFixedWidth,
  convertFixedWidthToCsv,
  type ColumnSpec,
} from "heroparser";
import { downloadText } from "../utils/download";

const SAMPLE_FIXED_WIDTH = [
  "Alice     00030  95000.50Developer   ",
  "Bob       00025  78500.00Designer    ",
  "Charlie   00034 112000.00Tech Lead   ",
].join("\n");

const SAMPLE_SPECS = `[
  { "name": "Name", "start": 0, "length": 10, "alignment": "left", "padChar": " " },
  { "name": "Age", "start": 10, "length": 5, "alignment": "right", "padChar": "0" },
  { "name": "Salary", "start": 15, "length": 10, "alignment": "right", "padChar": " " },
  { "name": "Role", "start": 25, "length": 12, "alignment": "left", "padChar": " " }
]`;

const SAMPLE_JSON_FOR_FW = JSON.stringify(
  [
    { Name: "Alice", Age: 30, Salary: 95000.5, Role: "Developer" },
    { Name: "Bob", Age: 25, Salary: 78500, Role: "Designer" },
    { Name: "Charlie", Age: 34, Salary: 112000, Role: "Tech Lead" },
  ],
  null,
  2,
);

const SAMPLE_CSV_FOR_FW = [
  "Name,Age,Salary,Role",
  "Alice,30,95000.50,Developer",
  "Bob,25,78500.00,Designer",
  "Charlie,34,112000.00,Tech Lead",
].join("\n");

export function useFixedWidth() {
  const fwInput = ref(SAMPLE_FIXED_WIDTH);
  const fwSpecs = ref(SAMPLE_SPECS);
  const fwDelimiter = ref(",");
  const fwIncludeHeader = ref(true);
  const fwOutputTitle = ref("JSON Output");
  const fwOutput = ref('Click "Parse Fixed-Width" or choose a conversion action...');
  const fwTime = ref("-");
  const fwCount = ref("-");
  const fwOutputKind = ref<"json" | "text" | "csv">("json");

  const parseSpecs = (): ColumnSpec[] | null => {
    try {
      const parsed = JSON.parse(fwSpecs.value);
      if (!Array.isArray(parsed) || parsed.length === 0) {
        fwOutputKind.value = "json";
        fwOutputTitle.value = "Specification Error";
        fwOutput.value = "Error: Column specification must be a non-empty JSON array.";
        return null;
      }
      return parsed;
    } catch (err: any) {
      fwOutputKind.value = "json";
      fwOutputTitle.value = "Specification Error";
      fwOutput.value = `Error: Invalid specification JSON format.\n${err.message}`;
      return null;
    }
  };

  const countNonEmptyLines = (text: string): number =>
    text.split(/\r?\n/).filter((line) => line.trim().length > 0).length;

  const runFixedWidthParse = () => {
    const specs = parseSpecs();
    if (!specs) return;

    const t0 = performance.now();
    try {
      const result = readFixedWidth(fwInput.value, specs);
      const t1 = performance.now();
      fwOutputKind.value = "json";
      fwOutputTitle.value = "JSON Output";
      fwOutput.value = JSON.stringify(result, null, 2);
      fwTime.value = `${(t1 - t0).toFixed(2)} ms`;
      fwCount.value = result.length.toString();
    } catch (err: any) {
      fwOutputKind.value = "json";
      fwOutputTitle.value = "Parsing Error";
      fwOutput.value = `Parsing Error: ${err.message}`;
    }
  };

  const runFixedWidthWrite = () => {
    const specs = parseSpecs();
    if (!specs) return;

    const t0 = performance.now();
    try {
      const trimmed = fwInput.value.trimStart();
      const records = trimmed.startsWith("[")
        ? JSON.parse(fwInput.value)
        : readFixedWidth(fwInput.value, specs);
      const formatted = writeFixedWidth(records, specs);
      const t1 = performance.now();
      fwOutputKind.value = "text";
      fwOutputTitle.value = "Formatted Fixed-Width Output";
      fwOutput.value = formatted;
      fwTime.value = `${(t1 - t0).toFixed(2)} ms`;
      fwCount.value = countNonEmptyLines(formatted).toString();
    } catch (err: any) {
      fwOutputKind.value = "json";
      fwOutputTitle.value = "Write Error";
      fwOutput.value = `Write Error: ${err.message}`;
    }
  };

  const runFixedWidthToCsv = () => {
    const specs = parseSpecs();
    if (!specs) return;

    const t0 = performance.now();
    try {
      const csv = convertFixedWidthToCsv(fwInput.value, specs, {
        delimiter: fwDelimiter.value || ",",
        includeHeader: fwIncludeHeader.value,
      });
      const t1 = performance.now();
      const lineCount = countNonEmptyLines(csv);
      fwOutputKind.value = "csv";
      fwOutputTitle.value = "CSV Output (from Fixed-Width)";
      fwOutput.value = csv;
      fwTime.value = `${(t1 - t0).toFixed(2)} ms`;
      fwCount.value = (
        fwIncludeHeader.value && lineCount > 0 ? lineCount - 1 : lineCount
      ).toString();
    } catch (err: any) {
      fwOutputKind.value = "json";
      fwOutputTitle.value = "Conversion Error";
      fwOutput.value = `Conversion Error: ${err.message}`;
    }
  };

  const runCsvToFixedWidth = () => {
    const specs = parseSpecs();
    if (!specs) return;

    const t0 = performance.now();
    try {
      const trimmed = fwInput.value.trimStart();
      if (trimmed.startsWith("[")) {
        fwOutputKind.value = "json";
        fwOutputTitle.value = "Input Hint";
        fwOutput.value =
          'Input looks like JSON. Use "Write Fixed-Width" for JSON arrays, or click "Load CSV Sample" before "CSV → Fixed-Width".';
        return;
      }

      const fw = convertCsvToFixedWidth(fwInput.value, specs, {
        delimiter: fwDelimiter.value || ",",
        includeHeader: fwIncludeHeader.value,
      });
      const t1 = performance.now();
      const lineCount = countNonEmptyLines(fw);
      fwOutputKind.value = "text";
      fwOutputTitle.value = "Fixed-Width Output (from CSV)";
      fwOutput.value = fw;
      fwTime.value = `${(t1 - t0).toFixed(2)} ms`;
      fwCount.value = (
        fwIncludeHeader.value && lineCount > 0 ? lineCount - 1 : lineCount
      ).toString();
    } catch (err: any) {
      fwOutputKind.value = "json";
      fwOutputTitle.value = "Conversion Error";
      fwOutput.value = `Conversion Error: ${err.message}`;
    }
  };

  const loadFixedWidthSample = () => {
    fwInput.value = SAMPLE_FIXED_WIDTH;
    fwSpecs.value = SAMPLE_SPECS;
  };

  const loadFixedWidthJsonSample = () => {
    fwInput.value = SAMPLE_JSON_FOR_FW;
    fwSpecs.value = SAMPLE_SPECS;
  };

  const loadFixedWidthCsvSample = () => {
    fwInput.value = SAMPLE_CSV_FOR_FW;
    fwSpecs.value = SAMPLE_SPECS;
  };

  const downloadFixedWidthOutput = () => {
    if (fwOutputKind.value === "csv") {
      downloadText(fwOutput.value, "fixedwidth-to-csv.csv", "text/csv;charset=utf-8");
      return;
    }
    if (fwOutputKind.value === "text") {
      downloadText(fwOutput.value, "heroparser-fixedwidth.txt");
    }
  };

  return {
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
  };
}
