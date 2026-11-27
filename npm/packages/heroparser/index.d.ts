export type FixedWidthAlignment = "left" | "right" | "center" | "none";

export interface CsvOptions {
  delimiter?: string;
  hasHeader?: boolean;
  allowNewlinesInsideQuotes?: boolean;
}

export interface ColumnSpec {
  name: string;
  start: number;
  length: number;
  alignment?: FixedWidthAlignment;
  padChar?: string;
}

export type FixedWidthWriteColumnSpec = Omit<ColumnSpec, "start"> & { start?: number };

export interface FixedWidthConversionOptions {
  delimiter?: string;
  includeHeader?: boolean;
}

export interface ExcelSheetDefinition<T extends object = Record<string, unknown>> {
  name: string;
  records: readonly T[] | string;
}

export interface CsvValidationOptions {
  delimiter?: string;
  hasHeader?: boolean;
  allowMissingColumns?: boolean;
  maxErrors?: number;
}

export interface CsvValidationError {
  errorType: string;
  message: string;
  rowNumber: number;
  columnNumber: number;
  expected?: string | null;
  actual?: string | null;
}

export interface CsvValidationResult {
  isValid: boolean;
  totalRows: number;
  columnCount: number;
  delimiter: string;
  stoppedEarly: boolean;
  headers: string[];
  errors: CsvValidationError[];
}

export interface CsvSchemaInferenceOptions {
  delimiter?: string;
  sampleRows?: number;
  distinguishTemporalTypes?: boolean;
}

export interface CsvInferredColumn {
  name: string;
  inferredType: string;
  isNullable: boolean;
  maxLength: number;
}

export interface CsvSchemaResult {
  sampledRowCount: number;
  columns: CsvInferredColumn[];
}

export interface LlmRepairOptions {
  quote?: string;
  escape?: string | null;
}

export interface HtbEncodeOptions {
  delimiter?: string;
  hasHeader?: boolean;
  distinguishTemporalTypes?: boolean;
}

export interface HtbColumnInfo {
  name: string;
  dataType: string;
  isNullable: boolean;
}

export interface HtbInspectResult {
  byteLength: number;
  recordCount: number;
  columns: HtbColumnInfo[];
}

export function init(): Promise<void>;
export function readCsv(csvText: string, options: CsvOptions & { hasHeader: false }): string[][];
export function readCsv<T = Record<string, string>>(
  csvText: string,
  options?: Omit<CsvOptions, "hasHeader"> & { hasHeader?: true | undefined },
): T[];
export function readCsv<T = Record<string, string>>(
  csvText: string,
  options?: CsvOptions,
): T[] | string[][];
export function readFixedWidth<T = Record<string, string>>(text: string, specs: readonly ColumnSpec[]): T[];
export function readExcel(excelBytes: Uint8Array, sheetName: string | undefined, hasHeader: false): string[][];
export function readExcel<T = Record<string, string>>(
  excelBytes: Uint8Array,
  sheetName?: string,
  hasHeader?: true | undefined,
): T[];
export function readExcel<T = Record<string, string>>(
  excelBytes: Uint8Array,
  sheetName?: string,
  hasHeader?: boolean,
): T[] | string[][];
export function getExcelSheetNames(excelBytes: Uint8Array): string[];
export function detectCsvDelimiter(sampleRows: string): string;

export function writeCsv<T extends object = Record<string, unknown>>(records: readonly T[], options?: CsvOptions): string;
export function writeFixedWidth<T extends object = Record<string, unknown>>(
  records: readonly T[],
  specs: readonly FixedWidthWriteColumnSpec[],
): string;
export function convertCsvToFixedWidth(
  csvText: string,
  specs: readonly FixedWidthWriteColumnSpec[],
  options?: FixedWidthConversionOptions,
): string;
export function convertFixedWidthToCsv(
  fixedWidthText: string,
  specs: readonly ColumnSpec[],
  options?: FixedWidthConversionOptions,
): string;
export function writeExcel<T extends object = Record<string, unknown>>(
  records: readonly T[] | string,
  sheetName?: string,
  hasHeader?: boolean,
): Uint8Array;
export function writeExcelMultiSheet(
  sheets: readonly ExcelSheetDefinition<object>[] | string,
  hasHeader?: boolean,
): Uint8Array;

export function validateCsv(csvText: string, options?: CsvValidationOptions): CsvValidationResult;
export function inferCsvSchema(
  csvText: string,
  options?: CsvSchemaInferenceOptions,
): CsvSchemaResult;
export function repairLlmCsv(text: string, options?: LlmRepairOptions): string;
export function convertCsvToJsonl(csvText: string, options?: CsvOptions): string;

export type JsonlInputFormat = "jsonl" | "json-array";

export function readJsonl<T = Record<string, unknown>>(jsonlText: string): T[];
export function readJsonlAsJson(jsonlText: string, options?: { indented?: boolean }): string;
export function writeJsonl(records: readonly unknown[] | string, inputFormat?: JsonlInputFormat): string;
export function convertJsonlToCsv(
  jsonlText: string,
  options?: Pick<CsvOptions, "delimiter">,
): string;

export function convertCsvToHtb(csvText: string, options?: HtbEncodeOptions): Uint8Array;
export function convertHtbToCsv(htbBytes: Uint8Array, options?: CsvOptions): string;
export function inspectHtb(htbBytes: Uint8Array): HtbInspectResult;
