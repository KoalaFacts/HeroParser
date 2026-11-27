import { ref } from "vue";
import { convertCsvToHtb, convertHtbToCsv, inspectHtb } from "heroparser";
import { downloadBytes, downloadText } from "../utils/download";

const SAMPLE_HTB_CSV = [
  "Id,Name,Score,Active,HireDate,Notes",
  "1,Alice,98.50,true,2022-03-15,Core SIMD engine",
  "2,Bob,,false,2024-07-01,Browser playground",
  '3,Charlie,91.25,true,2020-11-20,"Binary HTB round-trip"',
].join("\n");

function formatHexPreview(bytes: Uint8Array, limit = 32): string {
  const slice = bytes.subarray(0, Math.min(bytes.length, limit));
  return Array.from(slice, (b) => b.toString(16).padStart(2, "0")).join(" ");
}

export function useHtb() {
  const htbCsvInput = ref(SAMPLE_HTB_CSV);
  const htbDelimiter = ref(",");
  const htbHasHeader = ref(true);
  const htbDistinguishTemporal = ref(true);
  const htbFileInfo = ref("");
  const htbError = ref("");
  const htbOutputTitle = ref("HTB Binary Inspection");
  const htbOutput = ref(
    'Click "Encode CSV → HTB" or upload an .htb binary file to inspect schema and round-trip...',
  );
  const htbTime = ref("-");
  const htbCount = ref("-");
  const htbSizeSummary = ref("-");
  const hasHtbBytes = ref(false);
  const htbOutputKind = ref<"info" | "csv">("info");

  let loadedHtbBytes: Uint8Array | null = null;
  let loadedHtbFilename = "heroparser-data.htb";
  let loadedHtbRecordCount: number | null = null;

  const inspectCurrentBytes = (sourceLabel?: string) => {
    if (!loadedHtbBytes) {
      htbError.value = "No HTB binary loaded. Encode CSV → HTB or upload an .htb file first.";
      return;
    }

    htbError.value = "";
    const t0 = performance.now();
    try {
      const info = inspectHtb(loadedHtbBytes);
      const t1 = performance.now();
      loadedHtbRecordCount = info.recordCount;
      htbOutputKind.value = "info";
      htbOutputTitle.value = sourceLabel ?? "HTB Schema & Binary Inspection";
      htbOutput.value = JSON.stringify(
        {
          magicHexPrefix: formatHexPreview(loadedHtbBytes),
          ...info,
        },
        null,
        2,
      );
      htbTime.value = `${(t1 - t0).toFixed(2)} ms`;
      htbCount.value = info.recordCount.toString();
      if (htbSizeSummary.value === "-") {
        htbSizeSummary.value = `${loadedHtbBytes.byteLength} B binary`;
      }
    } catch (err: any) {
      htbError.value = `Inspection Error: ${err.message}`;
    }
  };

  const runCsvToHtb = () => {
    htbError.value = "";
    const t0 = performance.now();
    try {
      const csvBytes = new TextEncoder().encode(htbCsvInput.value).byteLength;
      const bytes = convertCsvToHtb(htbCsvInput.value, {
        delimiter: htbDelimiter.value || undefined,
        hasHeader: htbHasHeader.value,
        distinguishTemporalTypes: htbDistinguishTemporal.value,
      });
      const t1 = performance.now();

      loadedHtbBytes = bytes;
      loadedHtbRecordCount = null;
      loadedHtbFilename = "heroparser-data.htb";
      hasHtbBytes.value = bytes.byteLength > 0;
      htbFileInfo.value = `Encoded: ${loadedHtbFilename} (${bytes.byteLength} bytes)`;
      const deltaPct =
        csvBytes > 0 ? (((bytes.byteLength - csvBytes) / csvBytes) * 100).toFixed(1) : "0.0";
      htbSizeSummary.value = `${bytes.byteLength} B HTB vs ${csvBytes} B CSV (${deltaPct}%)`;

      inspectCurrentBytes("HTB Binary Encoded & Verified");
      htbTime.value = `${(t1 - t0).toFixed(2)} ms`;
    } catch (err: any) {
      htbError.value = `Encoding Error: ${err.message}`;
    }
  };

  const runHtbToCsv = () => {
    if (!loadedHtbBytes) {
      htbError.value = "No HTB binary loaded. Encode CSV → HTB or upload an .htb file first.";
      return;
    }

    htbError.value = "";
    const t0 = performance.now();
    try {
      const recordCount = loadedHtbRecordCount ?? inspectHtb(loadedHtbBytes).recordCount;
      loadedHtbRecordCount = recordCount;
      const csv = convertHtbToCsv(loadedHtbBytes, {
        delimiter: htbDelimiter.value || ",",
        hasHeader: htbHasHeader.value,
      });
      const t1 = performance.now();
      htbOutputKind.value = "csv";
      htbOutputTitle.value = "Decoded CSV Output (from HTB)";
      htbOutput.value = csv;
      htbTime.value = `${(t1 - t0).toFixed(2)} ms`;
      htbCount.value = recordCount.toString();
    } catch (err: any) {
      htbError.value = `Decoding Error: ${err.message}`;
    }
  };

  const runHtbInspect = () => {
    inspectCurrentBytes("HTB Schema & Binary Inspection");
  };

  const loadHtbFile = (file: File) => {
    htbError.value = "";
    loadedHtbFilename = file.name;
    const reader = new FileReader();
    reader.onload = (ev: ProgressEvent<FileReader>) => {
      const result = ev.target?.result;
      if (!(result instanceof ArrayBuffer)) return;
      loadedHtbBytes = new Uint8Array(result);
      loadedHtbRecordCount = null;
      hasHtbBytes.value = true;
      htbFileInfo.value = `Loaded: ${file.name} (${file.size} bytes)`;
      htbSizeSummary.value = `${loadedHtbBytes.byteLength} B binary`;
      inspectCurrentBytes(`Inspecting ${file.name}`);
    };
    reader.readAsArrayBuffer(file);
  };

  const handleHtbSelect = (e: Event) => {
    const target = e.target as HTMLInputElement | null;
    const files = target?.files;
    if (files && files.length > 0) {
      loadHtbFile(files[0]);
    }
  };

  const handleHtbDrop = (e: DragEvent) => {
    e.preventDefault();
    const files = e.dataTransfer?.files;
    if (files && files.length > 0) {
      loadHtbFile(files[0]);
    }
  };

  const loadHtbSample = () => {
    htbCsvInput.value = SAMPLE_HTB_CSV;
    htbDelimiter.value = ",";
    htbHasHeader.value = true;
    htbDistinguishTemporal.value = true;
  };

  const downloadHtb = () => {
    if (!loadedHtbBytes) return;
    downloadBytes(loadedHtbBytes, loadedHtbFilename);
  };

  const downloadHtbCsv = () => {
    if (htbOutputKind.value !== "csv") return;
    downloadText(htbOutput.value, "htb-decoded.csv", "text/csv;charset=utf-8");
  };

  return {
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
  };
}
