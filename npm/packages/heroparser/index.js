let exports = null;
let initialized = false;

export async function init() {
  if (initialized) return;

  const base =
    typeof document !== "undefined" ? document.baseURI || window.location.href : import.meta.url;
  const dotnetUrl = new URL("./_framework/dotnet.js", base).href;

  const { dotnet } = await import(/* @vite-ignore */ dotnetUrl);

  const { getAssemblyExports } = await dotnet.withDiagnosticTracing(false).create();

  exports = await getAssemblyExports("HeroParser.Wasm.dll");
  initialized = true;
}

function ensureInitialized() {
  if (!initialized || !exports) {
    throw new Error("HeroParser.Wasm is not initialized. Please await init() first.");
  }
}

export function readCsv(csvText, options = {}) {
  ensureInitialized();
  const resultJson = exports.HeroParser.Wasm.HeroParserWasm.ParseCsvToJson(
    csvText,
    JSON.stringify(options),
  );
  return JSON.parse(resultJson);
}

export function readFixedWidth(text, specs = []) {
  ensureInitialized();
  const resultJson = exports.HeroParser.Wasm.HeroParserWasm.ParseFixedWidthToJson(
    text,
    JSON.stringify(specs),
  );
  return JSON.parse(resultJson);
}

export function readExcel(excelBytes, sheetName = "", hasHeader = true) {
  ensureInitialized();
  // Uint8Array maps cleanly to C# byte[] in .NET WebAssembly interop
  const resultJson = exports.HeroParser.Wasm.HeroParserWasm.ParseExcelToJson(
    excelBytes,
    sheetName,
    hasHeader,
  );
  return JSON.parse(resultJson);
}

export function getExcelSheetNames(excelBytes) {
  ensureInitialized();
  const resultJson = exports.HeroParser.Wasm.HeroParserWasm.GetExcelSheetNames(excelBytes);
  return JSON.parse(resultJson);
}

export function detectCsvDelimiter(sampleRows) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.DetectCsvDelimiter(sampleRows);
}

export function writeCsv(records, options = {}) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.WriteCsv(
    JSON.stringify(records),
    JSON.stringify(options),
  );
}

export function writeFixedWidth(records, specs = []) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.WriteFixedWidth(
    JSON.stringify(records),
    JSON.stringify(specs),
  );
}

export function convertCsvToFixedWidth(csvText, specs = [], options = {}) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.ConvertCsvToFixedWidth(
    csvText,
    JSON.stringify(specs),
    options?.delimiter ?? null,
    options?.includeHeader ?? false,
  );
}

export function convertFixedWidthToCsv(fixedWidthText, specs = [], options = {}) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.ConvertFixedWidthToCsv(
    fixedWidthText,
    JSON.stringify(specs),
    options?.delimiter ?? null,
    options?.includeHeader ?? true,
  );
}

export function writeExcel(records, sheetName = "Sheet1", hasHeader = true) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.WriteExcel(
    typeof records === "string" ? records : JSON.stringify(records),
    sheetName,
    hasHeader,
  );
}

export function writeExcelMultiSheet(sheets, hasHeader = true) {
  ensureInitialized();
  let sheetsJson;
  if (typeof sheets === "string") {
    sheetsJson = sheets;
  } else {
    const serialized = sheets.map((sheet) => {
      if (sheet === null || typeof sheet !== "object") {
        return JSON.stringify(sheet);
      }
      const recordsJson =
        typeof sheet.records === "string" ? sheet.records : JSON.stringify(sheet.records ?? []);
      return `{"name":${JSON.stringify(sheet.name ?? "")},"records":${recordsJson}}`;
    });
    sheetsJson = `[${serialized.join(",")}]`;
  }
  return exports.HeroParser.Wasm.HeroParserWasm.WriteExcelMultiSheet(sheetsJson, hasHeader);
}

export function validateCsv(csvText, options = {}) {
  ensureInitialized();
  const resultJson = exports.HeroParser.Wasm.HeroParserWasm.ValidateCsvToJson(
    csvText,
    options?.delimiter ?? null,
    options?.hasHeader ?? true,
    options?.allowMissingColumns ?? false,
    options?.maxErrors ?? 100,
  );
  return JSON.parse(resultJson);
}

export function inferCsvSchema(csvText, options = {}) {
  ensureInitialized();
  const resultJson = exports.HeroParser.Wasm.HeroParserWasm.InferCsvSchemaToJson(
    csvText,
    options?.delimiter ?? null,
    options?.sampleRows ?? 100,
    options?.distinguishTemporalTypes ?? false,
  );
  return JSON.parse(resultJson);
}

export function repairLlmCsv(text, options = {}) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.RepairLlmCsv(
    text,
    options?.quote ?? null,
    options?.escape === undefined ? null : (options.escape ?? ""),
  );
}

export function convertCsvToJsonl(csvText, options = {}) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.ConvertCsvToJsonl(
    csvText,
    options?.delimiter ?? null,
    options?.hasHeader ?? true,
    options?.allowNewlinesInsideQuotes ?? false,
  );
}

export function readJsonl(jsonlText) {
  ensureInitialized();
  const resultJson = exports.HeroParser.Wasm.HeroParserWasm.ParseJsonlToJson(jsonlText);
  return JSON.parse(resultJson);
}

export function readJsonlAsJson(jsonlText, options = {}) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.ParseJsonlToFormattedJson(
    jsonlText,
    options?.indented ?? false,
  );
}

export function writeJsonl(records, inputFormat) {
  ensureInitialized();
  if (typeof records === "string") {
    return exports.HeroParser.Wasm.HeroParserWasm.RewriteJsonl(records, inputFormat ?? "jsonl");
  }
  return exports.HeroParser.Wasm.HeroParserWasm.WriteJsonl(JSON.stringify(records));
}

export function convertJsonlToCsv(jsonlText, options = {}) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.ConvertJsonlToCsv(
    jsonlText,
    options?.delimiter ?? null,
  );
}

export function convertCsvToHtb(csvText, options = {}) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.ConvertCsvToHtb(
    csvText,
    options?.delimiter ?? null,
    options?.hasHeader ?? true,
    options?.distinguishTemporalTypes ?? true,
  );
}

export function convertHtbToCsv(htbBytes, options = {}) {
  ensureInitialized();
  return exports.HeroParser.Wasm.HeroParserWasm.ConvertHtbToCsv(
    htbBytes,
    options?.delimiter ?? null,
    options?.hasHeader ?? true,
  );
}

export function inspectHtb(htbBytes) {
  ensureInitialized();
  const resultJson = exports.HeroParser.Wasm.HeroParserWasm.InspectHtbSchemaToJson(htbBytes);
  return JSON.parse(resultJson);
}
