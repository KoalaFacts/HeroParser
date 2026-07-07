let exports = null;
let initialized = false;

export async function init() {
    if (initialized) return;

    const frameworkDir = '_framework';
    const loaderName = 'dotnet.js';
    let dotnetUrl = `./${frameworkDir}/${loaderName}`;

    if (typeof document !== 'undefined') {
        const base = document.baseURI || window.location.href;
        dotnetUrl = new URL(`${frameworkDir}/${loaderName}`, base).href;
    } else {
        const metaUrl = import.meta.url;
        dotnetUrl = new URL(`./${frameworkDir}/${loaderName}`, metaUrl).href;
    }

    const importShim = new Function('url', 'return import(url)');
    const { dotnet } = await importShim(dotnetUrl);

    const { getAssemblyExports, getConfig } = await dotnet
        .withDiagnosticTracing(false)
        .create();

    const config = getConfig();
    exports = await getAssemblyExports("HeroParser.Wasm.dll");
    initialized = true;
}

function ensureInitialized() {
    if (!initialized || !exports) {
        throw new Error("HeroParser.Wasm is not initialized. Please await init() first.");
    }
}

export function parseCsv(csvText, options = {}) {
    ensureInitialized();
    const resultJson = exports.HeroParser.Wasm.HeroParserWasm.ParseCsvToJson(csvText, JSON.stringify(options));
    return JSON.parse(resultJson);
}

export function parseFixedWidth(text, specs = []) {
    ensureInitialized();
    const resultJson = exports.HeroParser.Wasm.HeroParserWasm.ParseFixedWidthToJson(text, JSON.stringify(specs));
    return JSON.parse(resultJson);
}

export function parseExcel(excelBytes, sheetName = "", hasHeader = true) {
    ensureInitialized();
    // Uint8Array maps cleanly to C# byte[] in .NET WebAssembly interop
    const resultJson = exports.HeroParser.Wasm.HeroParserWasm.ParseExcelToJson(excelBytes, sheetName, hasHeader);
    return JSON.parse(resultJson);
}

export function detectCsvDelimiter(sampleRows) {
    ensureInitialized();
    return exports.HeroParser.Wasm.HeroParserWasm.DetectCsvDelimiter(sampleRows);
}

export function repairTabularOutput(rawText) {
    ensureInitialized();
    return exports.HeroParser.Wasm.HeroParserWasm.RepairTabularOutput(rawText);
}

export function writeCsv(records, options = {}) {
    ensureInitialized();
    return exports.HeroParser.Wasm.HeroParserWasm.WriteCsv(JSON.stringify(records), JSON.stringify(options));
}

export function writeFixedWidth(records, specs = []) {
    ensureInitialized();
    return exports.HeroParser.Wasm.HeroParserWasm.WriteFixedWidth(JSON.stringify(records), JSON.stringify(specs));
}

export function writeExcel(records, sheetName = "Sheet1", hasHeader = true) {
    ensureInitialized();
    return exports.HeroParser.Wasm.HeroParserWasm.WriteExcel(JSON.stringify(records), sheetName, hasHeader);
}

// Consistent Read/Write naming aliases
export const readCsv = parseCsv;
export const readFixedWidth = parseFixedWidth;
export const readExcel = parseExcel;
