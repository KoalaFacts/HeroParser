import { dotnet } from './_framework/dotnet.js';

let exports = null;
let initialized = false;

export async function init() {
    if (initialized) return;

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
