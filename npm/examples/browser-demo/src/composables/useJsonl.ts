import { ref } from "vue";
import { readJsonl, readJsonlAsJson, writeJsonl, convertJsonlToCsv, type JsonlInputFormat } from "heroparser";
import { downloadText } from "../utils/download";

const SAMPLE_JSONL = [
  '{"id":1,"name":"Alice","role":"Developer","score":98.5,"active":true}',
  '{"id":2,"name":"Bob","role":"Designer","score":89.0,"active":false}',
  '{"id":3,"name":"Charlie","role":"Tech Lead","score":95.25,"team":"Core"}',
].join("\n");

const SAMPLE_JSON_ARRAY = JSON.stringify(
  [
    { id: 101, model: "Gemma-4", latencyMs: 12.4, ok: true },
    { id: 102, model: "HeroParser", latencyMs: 0.18, ok: true, simd: "Scalar-WASM" },
  ],
  null,
  2,
);

export function useJsonl() {
  const jsonlInput = ref(SAMPLE_JSONL);
  const jsonlInputFormat = ref<JsonlInputFormat>("jsonl");
  const jsonlDelimiter = ref(",");
  const jsonlOutputTitle = ref("JSON Output");
  const jsonlOutput = ref('Click "Parse JSONL", "JSONL → CSV", or "Write JSONL" to see results...');
  const jsonlTime = ref("-");
  const jsonlCount = ref("-");
  const jsonlOutputKind = ref<"json" | "jsonl" | "csv">("json");

  const runJsonlParse = () => {
    const t0 = performance.now();
    try {
      const prettyJson = readJsonlAsJson(jsonlInput.value, { indented: true });
      const t1 = performance.now();
      const count = prettyJson === "[]" ? 0 : JSON.parse(prettyJson).length;
      jsonlOutputKind.value = "json";
      jsonlOutputTitle.value = "Parsed JSON Array";
      jsonlOutput.value = prettyJson;
      jsonlTime.value = `${(t1 - t0).toFixed(2)} ms`;
      jsonlCount.value = count.toString();
    } catch (err: any) {
      jsonlOutputKind.value = "json";
      jsonlOutputTitle.value = "Parsing Error";
      jsonlOutput.value = `Parsing Error: ${err.message}`;
    }
  };

  const runJsonlToCsv = () => {
    const t0 = performance.now();
    try {
      const csv = convertJsonlToCsv(jsonlInput.value, {
        delimiter: jsonlDelimiter.value || ",",
      });
      const t1 = performance.now();
      const recordCount = csv.trim().length === 0 ? 0 : readJsonl(jsonlInput.value).length;
      jsonlOutputKind.value = "csv";
      jsonlOutputTitle.value = "CSV Output (Union Schema)";
      jsonlOutput.value = csv;
      jsonlTime.value = `${(t1 - t0).toFixed(2)} ms`;
      jsonlCount.value = recordCount.toString();
    } catch (err: any) {
      jsonlOutputKind.value = "json";
      jsonlOutputTitle.value = "Conversion Error";
      jsonlOutput.value = `Conversion Error: ${err.message}`;
    }
  };

  const runJsonlWrite = () => {
    const t0 = performance.now();
    try {
      const output = writeJsonl(jsonlInput.value, jsonlInputFormat.value);
      const t1 = performance.now();
      const lines = output.split(/\r?\n/).filter((l) => l.trim().length > 0).length;
      jsonlOutputKind.value = "jsonl";
      jsonlOutputTitle.value = "JSONL Output";
      jsonlOutput.value = output;
      jsonlTime.value = `${(t1 - t0).toFixed(2)} ms`;
      jsonlCount.value = lines.toString();
    } catch (err: any) {
      jsonlOutputKind.value = "json";
      jsonlOutputTitle.value = "Write Error";
      jsonlOutput.value = `Write Error: ${err.message}`;
    }
  };

  const loadJsonlSample = () => {
    jsonlInput.value = SAMPLE_JSONL;
    jsonlInputFormat.value = "jsonl";
  };

  const loadJsonArrayPreset = () => {
    jsonlInput.value = SAMPLE_JSON_ARRAY;
    jsonlInputFormat.value = "json-array";
  };

  const downloadJsonlOutput = () => {
    if (jsonlOutputKind.value === "csv") {
      downloadText(jsonlOutput.value, "jsonl-to-csv.csv", "text/csv;charset=utf-8");
      return;
    }
    if (jsonlOutputKind.value === "jsonl") {
      downloadText(
        jsonlOutput.value,
        "heroparser-output.jsonl",
        "application/x-ndjson;charset=utf-8",
      );
    }
  };

  return {
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
  };
}
