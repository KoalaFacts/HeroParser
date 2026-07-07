# heroparser

High-performance, zero-allocation tabular data parser (CSV, Fixed-Width, Excel) for JavaScript/TypeScript environments (Node.js and Browser) powered by .NET WebAssembly.

## Features

* **High Performance**: Leverages the SIMD-accelerated C# HeroParser engine via compiled WebAssembly.
* **Unified Formats**: Support for CSV, Fixed-Width, and Excel (.xlsx) files under a single lightweight package.
* **Smart Delimiter Detection**: Instantly analyzes CSV samples to detect the correct column delimiter.
* **AI-Native Repair**: Built-in support to clean and repair incomplete, truncated, or markdown-wrapped tabular LLM outputs.
* **Fully Portable**: Runs inside any modern web browser or Node.js environment.

---

## Installation

```bash
npm install heroparser
```

## Interactive Playground Demo

Try the interactive, zero-allocation WebAssembly sandbox directly in your browser:
👉 **[https://KoalaFacts.github.io/HeroParser/demo/](https://KoalaFacts.github.io/HeroParser/demo/)**

---

## Quick Start

### 1. Basic CSV Parsing

```javascript
import { init, parseCsv } from 'heroparser';

// Initialize the WebAssembly runtime
await init();

// Parse CSV string with headers
const records = parseCsv("Name,Age,Role\nAlice,30,Developer\nBob,25,Designer", {
    delimiter: ',',
    hasHeader: true
});

console.log(records);
// Output:
// [
//   { Name: 'Alice', Age: '30', Role: 'Developer' },
//   { Name: 'Bob', Age: '25', Role: 'Designer' }
// ]
```

### 2. Fixed-Width Parsing

```javascript
import { init, parseFixedWidth } from 'heroparser';

await init();

const specs = [
    { name: "Name", start: 0, length: 10 },
    { name: "Age", start: 10, length: 10 },
    { name: "Role", start: 20, length: 11 }
];

const text = "Alice     30        Developer \nBob       25        Designer  ";
const records = parseFixedWidth(text, specs);
```

### 3. Excel (.xlsx) Parsing

```javascript
import { init, parseExcel } from 'heroparser';

await init();

// Provide raw binary array (Uint8Array) of the .xlsx file
const records = parseExcel(excelBytesArray, "Sheet1", true);
```

### 4. Serializing & Writing (CSV, Fixed-Width, Excel)

```javascript
import { init, writeCsv, writeFixedWidth, writeExcel } from 'heroparser';

await init();

const records = [
    { Name: "Alice", Age: "30", Role: "Developer" },
    { Name: "Bob", Age: "25", Role: "Designer" }
];

// Write to CSV format
const csv = writeCsv(records, { delimiter: ',', hasHeader: true });

// Write to Fixed-Width format
const specs = [
    { name: "Name", start: 0, length: 10 },
    { name: "Age", start: 10, length: 5 },
    { name: "Role", start: 15, length: 15 }
];
const fwText = writeFixedWidth(records, specs);

// Write to Excel (.xlsx) file bytes (returns Uint8Array)
const excelBytes = writeExcel(records, "Sheet1", true);
```

---

## API Reference

### `init(): Promise<void>`
Initializes the underlying WebAssembly runtime. This must be awaited once before invoking other parsing methods.

### `parseCsv(csvText: string, options?: WasmCsvOptions): any[]`
Parses CSV text content into a JSON array.
* `options.delimiter`: Column delimiter character (e.g. `","`, `";"`). Defaults to auto-detect.
* `options.hasHeader`: Set `true` to map rows to object keys based on the header row, or `false` to return raw string arrays.

### `parseFixedWidth(text: string, specs: WasmColumnSpec[]): any[]`
Parses fixed-width column blocks based on the given boundaries.
* `specs`: Array of `{ name: string, start: number, length: number }`.

### `parseExcel(excelBytes: Uint8Array, sheetName?: string, hasHeader?: boolean): any[]`
Reads Excel spreadsheet workbook byte arrays and parses rows.

### `detectCsvDelimiter(sampleRows: string): string`
Analyzes a sample text chunk to identify the most confident CSV separator character.

### `repairTabularOutput(rawText: string): string`
Cleans up conversational and malformed LLM responses to extract valid raw CSV boundaries.

### `writeCsv(records: any[], options?: WasmCsvOptions): string`
Serializes a list of object records back to CSV format.

### `writeFixedWidth(records: any[], specs: WasmColumnSpec[]): string`
Serializes a list of object records back to fixed-width format.

### `writeExcel(records: any[], sheetName?: string, hasHeader?: boolean): Uint8Array`
Serializes a list of object records back to Excel (.xlsx) binary bytes.

### Consistent Read/Write Aliases
The package also exports semantic aliases for developers who prefer standard Read/Write terminology:
* `readCsv` (alias for `parseCsv`)
* `readFixedWidth` (alias for `parseFixedWidth`)
* `readExcel` (alias for `parseExcel`)

---

## License

MIT
