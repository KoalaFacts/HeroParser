import { ref } from "vue";
import {
  readExcel,
  getExcelSheetNames,
  writeExcel,
  writeExcelMultiSheet,
  writeCsv,
} from "heroparser";
import { downloadBytes, downloadText } from "../utils/download";

const DEFAULT_EXCEL_JSON = JSON.stringify(
  [
    { Region: "North", Category: "Hardware", Units: 120, Revenue: 14500.5, Verified: true },
    { Region: "South", Category: "Software", Units: 85, Revenue: 22990, Verified: true },
    { Region: "West", Category: "Services", Units: 40, Revenue: 8400, Verified: false },
  ],
  null,
  2,
);

const MULTI_SHEET_TEAM = [
  { Name: "Alice", Department: "Core Engine", Level: 5, Active: true },
  { Name: "Bob", Department: "Browser SDK", Level: 4, Active: true },
  { Name: "Charlie", Department: "Benchmarks", Level: 5, Active: false },
];

export function useExcel() {
  const excelSheetName = ref("");
  const excelSheetNames = ref<string[]>([]);
  const excelHasHeader = ref(true);
  const excelJsonInput = ref(DEFAULT_EXCEL_JSON);
  const excelFileInfo = ref("");
  const excelError = ref("");
  const excelOutputTitle = ref("JSON Output");
  const excelOutput = ref(
    "Upload an .xlsx workbook, build from JSON records, or generate a sample workbook to inspect sheets...",
  );
  const excelTime = ref("-");
  const excelCount = ref("-");
  const excelOutputKind = ref<"json" | "csv">("json");

  let loadedExcelBytes: Uint8Array | null = null;
  let loadedExcelFilename = "heroparser-sample.xlsx";

  const refreshSheetNames = () => {
    if (!loadedExcelBytes) {
      excelSheetNames.value = [];
      return;
    }
    try {
      excelSheetNames.value = getExcelSheetNames(loadedExcelBytes);
    } catch {
      excelSheetNames.value = [];
    }
  };

  const handleExcelSelect = (e: Event) => {
    const target = e.target as HTMLInputElement | null;
    const files = target?.files;
    if (files && files.length > 0) {
      loadExcelFile(files[0]);
    }
  };

  const handleExcelDrop = (e: DragEvent) => {
    e.preventDefault();
    const files = e.dataTransfer?.files;
    if (files && files.length > 0 && files[0].name.endsWith(".xlsx")) {
      loadExcelFile(files[0]);
    }
  };

  const loadExcelFile = (file: File) => {
    excelError.value = "";
    loadedExcelFilename = file.name;
    const reader = new FileReader();
    reader.onload = (ev: ProgressEvent<FileReader>) => {
      const result = ev.target?.result;
      if (!(result instanceof ArrayBuffer)) return;
      loadedExcelBytes = new Uint8Array(result);
      excelFileInfo.value = `Loaded: ${file.name} (${(file.size / 1024).toFixed(1)} KB)`;
      refreshSheetNames();
      if (
        excelSheetNames.value.length > 0 &&
        !excelSheetNames.value.includes(excelSheetName.value)
      ) {
        excelSheetName.value = excelSheetNames.value[0];
      }
      runExcelParse();
    };
    reader.readAsArrayBuffer(file);
  };

  const runExcelParse = () => {
    if (!loadedExcelBytes) {
      excelError.value = "No Excel file loaded.";
      return;
    }

    excelError.value = "";
    const t0 = performance.now();
    try {
      const result = readExcel(loadedExcelBytes, excelSheetName.value || "", excelHasHeader.value);
      const t1 = performance.now();
      excelOutputKind.value = "json";
      excelOutputTitle.value = excelSheetName.value
        ? `JSON Output — Sheet "${excelSheetName.value}"`
        : "JSON Output";
      excelOutput.value = JSON.stringify(result, null, 2);
      excelTime.value = `${(t1 - t0).toFixed(2)} ms`;
      excelCount.value = result.length.toString();
    } catch (err: any) {
      excelError.value = `Parsing Error: ${err.message}`;
      excelOutput.value = "Error parsing file.";
    }
  };

  const selectExcelSheet = (name: string) => {
    excelSheetName.value = name;
    runExcelParse();
  };

  const buildExcelFromJson = () => {
    excelError.value = "";
    try {
      const records = JSON.parse(excelJsonInput.value);
      const sheet = excelSheetName.value.trim() || "Sheet1";
      const t0 = performance.now();
      loadedExcelBytes = writeExcel(records, sheet, excelHasHeader.value);
      const t1 = performance.now();
      loadedExcelFilename = `${sheet.toLowerCase().replace(/[^a-z0-9-_]+/gi, "-") || "workbook"}.xlsx`;
      refreshSheetNames();
      excelSheetName.value = excelSheetNames.value[0] ?? sheet;
      excelFileInfo.value = `Built from JSON: ${loadedExcelFilename} (${(loadedExcelBytes.byteLength / 1024).toFixed(1)} KB)`;
      runExcelParse();
      excelTime.value = `${(t1 - t0).toFixed(2)} ms (write)`;
    } catch (err: any) {
      excelError.value = `Build Error: ${err.message}`;
    }
  };

  const loadSampleExcel = () => {
    excelError.value = "";
    try {
      const sampleRows = JSON.parse(DEFAULT_EXCEL_JSON);
      const sheet = excelSheetName.value.trim() || "Sales";
      loadedExcelBytes = writeExcel(sampleRows, sheet, excelHasHeader.value);
      loadedExcelFilename = "heroparser-sample.xlsx";
      refreshSheetNames();
      excelSheetName.value = excelSheetNames.value[0] ?? sheet;
      excelFileInfo.value = `Generated sample: ${loadedExcelFilename} (${(loadedExcelBytes.byteLength / 1024).toFixed(1)} KB)`;
      runExcelParse();
    } catch (err: any) {
      excelError.value = `Generation Error: ${err.message}`;
    }
  };

  const loadMultiSheetSample = () => {
    excelError.value = "";
    try {
      const salesRows = JSON.parse(excelJsonInput.value);
      const t0 = performance.now();
      loadedExcelBytes = writeExcelMultiSheet(
        [
          { name: "Sales", records: salesRows },
          { name: "Team", records: MULTI_SHEET_TEAM },
        ],
        excelHasHeader.value,
      );
      const t1 = performance.now();
      loadedExcelFilename = "heroparser-multisheet.xlsx";
      refreshSheetNames();
      excelSheetName.value = excelSheetNames.value[0] ?? "Sales";
      excelFileInfo.value = `Generated multi-sheet: ${loadedExcelFilename} (${(loadedExcelBytes.byteLength / 1024).toFixed(1)} KB)`;
      runExcelParse();
      excelTime.value = `${(t1 - t0).toFixed(2)} ms (write multi-sheet)`;
    } catch (err: any) {
      excelError.value = `Multi-Sheet Error: ${err.message}`;
    }
  };

  const exportExcelToCsv = () => {
    if (!loadedExcelBytes) {
      excelError.value = "No Excel file loaded.";
      return;
    }

    excelError.value = "";
    const t0 = performance.now();
    try {
      const rows = readExcel(loadedExcelBytes, excelSheetName.value || "", excelHasHeader.value);
      const csv = writeCsv(rows, { delimiter: ",", hasHeader: excelHasHeader.value });
      const t1 = performance.now();
      excelOutputKind.value = "csv";
      excelOutputTitle.value = excelSheetName.value
        ? `CSV Output — Sheet "${excelSheetName.value}"`
        : "CSV Output";
      excelOutput.value = csv;
      excelTime.value = `${(t1 - t0).toFixed(2)} ms`;
      excelCount.value = rows.length.toString();
    } catch (err: any) {
      excelError.value = `CSV Export Error: ${err.message}`;
    }
  };

  const downloadSampleExcel = () => {
    if (!loadedExcelBytes) {
      loadSampleExcel();
    }
    if (!loadedExcelBytes) {
      return;
    }

    downloadBytes(
      loadedExcelBytes,
      loadedExcelFilename,
      "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    );
  };

  const downloadExcelCsv = () => {
    if (excelOutputKind.value !== "csv") return;
    downloadText(excelOutput.value, "excel-export.csv", "text/csv;charset=utf-8");
  };

  return {
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
  };
}
