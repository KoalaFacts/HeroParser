# heroparser

Tabular data parser (CSV, Fixed-Width, Excel, JSONL, and HTB Binary) for JavaScript/TypeScript environments (Node.js and Browser) powered by .NET WebAssembly. This package is an unpublished preview in the repository.

## Features

- **The .NET engine in WebAssembly**: runs the C# HeroParser parsers compiled to WebAssembly, using the scalar fallback when hardware SIMD is unavailable in WebAssembly.
- **Unified Formats**: Read, write, and convert between **CSV**, **Fixed-Width** (with alignment & padding), **Excel (.xlsx)** (single and multi-sheet), **JSONL**, and HeroParser's compact **HTB** binary format under a single package.
- **Validation, Schema Inference & AI Repair**: Built-in `validateCsv`, `inferCsvSchema` (including `distinguishTemporalTypes`), `detectCsvDelimiter`, and `repairLlmCsv` for messy or LLM-generated CSV data.
- **Fully Portable**: Runs inside any modern web browser or Node.js environment.

---

## Local Preview

```bash
dotnet publish src/HeroParser.Wasm/HeroParser.Wasm.csproj -c Release
```

Run this from the repository root before using the workspace package. The package needs the generated `_framework` runtime assets; it is not yet available on npm.

## Interactive Playground Demo

Try the interactive WebAssembly sandbox directly in your browser:
👉 **[https://KoalaFacts.github.io/HeroParser/demo/](https://KoalaFacts.github.io/HeroParser/demo/)**

---

## Quick Start

### 1. Basic CSV Reading

```javascript
import { init, readCsv } from "heroparser";

// Initialize the WebAssembly runtime
await init();

// Read CSV string with headers
const records = readCsv("Name,Age,Role\nAlice,30,Developer\nBob,25,Designer", {
  delimiter: ",",
  hasHeader: true,
});
```

### 2. Fixed-Width Reading & Conversion

```javascript
import {
  init,
  readFixedWidth,
  writeFixedWidth,
  convertCsvToFixedWidth,
  convertFixedWidthToCsv,
} from "heroparser";

await init();

const specs = [
  { name: "Name", start: 0, length: 10, alignment: "left" },
  { name: "Age", start: 10, length: 5, alignment: "right", padChar: "0" },
  { name: "Role", start: 15, length: 12, alignment: "left" },
];

const text = "Alice     00030Developer   \nBob       00025Designer    ";
const records = readFixedWidth(text, specs);
const csv = convertFixedWidthToCsv(text, specs, { includeHeader: true });
const fw = convertCsvToFixedWidth(csv, specs, { includeHeader: false });
```

### 3. Excel (.xlsx) Multi-Sheet Reading & Writing

```javascript
import { init, readExcel, getExcelSheetNames, writeExcel, writeExcelMultiSheet } from "heroparser";

await init();

const workbookBytes = writeExcelMultiSheet([
  { name: "Sales", records: [{ Region: "North", Units: 120, Active: true }] },
  { name: "Team", records: [{ Name: "Alice", Role: "Developer" }] },
]);

const sheetNames = getExcelSheetNames(workbookBytes); // ["Sales", "Team"]
const teamRows = readExcel(workbookBytes, "Team", true);
```

### 4. JSONL & HTB Binary

```javascript
import {
  init,
  readJsonl,
  writeJsonl,
  convertCsvToJsonl,
  convertJsonlToCsv,
  convertCsvToHtb,
  inspectHtb,
  convertHtbToCsv,
} from "heroparser";

await init();

const csv = "Id,Score,Active\n1,98.5,true\n2,87.0,false";
const jsonl = convertCsvToJsonl(csv);
const jsonlRecords = readJsonl(jsonl);
const roundTripCsv = convertJsonlToCsv(writeJsonl(jsonlRecords));

const htbBytes = convertCsvToHtb(csv);
const metadata = inspectHtb(htbBytes);
const decodedCsv = convertHtbToCsv(htbBytes);
```

### 5. Validation, Schema Inference & LLM Repair

````javascript
import { init, validateCsv, inferCsvSchema, repairLlmCsv } from "heroparser";

await init();

const cleanCsv = repairLlmCsv('```csv\nName,Age,HireDate\nAlice,30,"2024-01-15');
const validation = validateCsv(cleanCsv, { hasHeader: true });
const schema = inferCsvSchema(cleanCsv, { sampleRows: 100, distinguishTemporalTypes: true });
````

---

## API Reference

### `init(): Promise<void>`

Initializes the underlying WebAssembly runtime. Must be awaited once before invoking any other method.

### CSV APIs

- `readCsv(csvText: string, options?: CsvOptions): any[]` — Reads CSV text into objects (`hasHeader: true`) or string arrays (`hasHeader: false`).
- `writeCsv(records: any[], options?: CsvOptions): string` — Serializes record objects (supporting mixed JS primitives) or cell arrays into CSV.
- `detectCsvDelimiter(sampleRows: string): string` — Detects the most probable separator character.
- `validateCsv(csvText: string, options?: CsvValidationOptions): CsvValidationResult` — Validates structural consistency and reports per-row errors.
- `inferCsvSchema(csvText: string, options?: CsvSchemaInferenceOptions): CsvSchemaResult` — Infers column types, nullability, and max lengths; set `distinguishTemporalTypes: true` to distinguish `DateOnly`, `TimeOnly`, `DateTimeOffset`, and `TimeSpan`.
- `repairLlmCsv(text: string, options?: LlmRepairOptions): string` — Strips markdown code fences and repairs unclosed trailing quotes.

### Fixed-Width APIs

- `readFixedWidth(text: string, specs: ColumnSpec[]): any[]` — Parses fixed-width lines honoring `start`, `length`, optional `alignment` (`"left" | "right" | "center" | "none"`), and `padChar`.
- `writeFixedWidth(records: any[], specs: ColumnSpec[]): string` — Serializes record objects into fixed-width text honoring alignment and `padChar`.
- `convertCsvToFixedWidth(csvText: string, specs: ColumnSpec[], options?: FixedWidthConversionOptions): string` — Converts header-matched CSV columns to fixed-width format.
- `convertFixedWidthToCsv(fixedWidthText: string, specs: ColumnSpec[], options?: FixedWidthConversionOptions): string` — Converts fixed-width text to CSV with RFC quoting.

### Excel (.xlsx) APIs

- `readExcel(excelBytes: Uint8Array, sheetName?: string, hasHeader?: boolean): any[]` — Reads rows from the named sheet (or first sheet if omitted).
- `getExcelSheetNames(excelBytes: Uint8Array): string[]` — Lists worksheet names in workbook order.
- `writeExcel(records: any[], sheetName?: string, hasHeader?: boolean): Uint8Array` — Writes a single-sheet `.xlsx` workbook (preserving numbers and booleans cleanly).
- `writeExcelMultiSheet(sheets: ExcelSheetDefinition[], hasHeader?: boolean): Uint8Array` — Writes a multi-sheet `.xlsx` workbook.

### JSONL & HTB Binary APIs

- `convertCsvToJsonl(csvText: string, options?: CsvOptions): string` — Converts CSV rows into flat JSONL lines.
- `readJsonl(jsonlText: string): any[]` — Parses JSONL lines into an array of JSON values.
- `writeJsonl(records: any[]): string` — Serializes an array of JSON values into JSONL.
- `convertJsonlToCsv(jsonlText: string, options?: Pick<CsvOptions, "delimiter">): string` — Infers union headers from leading JSONL records and converts to CSV.
- `convertCsvToHtb(csvText: string, options?: HtbEncodeOptions): Uint8Array` — Infers typed columns and encodes CSV into compact HTB binary bytes.
- `inspectHtb(htbBytes: Uint8Array): HtbInspectResult` — Inspects binary size, record count, and schema columns from an HTB payload.
- `convertHtbToCsv(htbBytes: Uint8Array, options?: CsvOptions): string` — Decodes HTB binary bytes back into CSV text.

---

## License

MIT
