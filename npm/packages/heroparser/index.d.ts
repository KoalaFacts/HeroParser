export type FixedWidthAlignment = "left" | "right" | "center" | "none";

export interface CsvOptions {
  delimiter?: string;
  hasHeader?: boolean;
}

export interface ColumnSpec {
  name: string;
  start: number;
  length: number;
  alignment?: FixedWidthAlignment;
  padChar?: string;
}

export interface FixedWidthConversionOptions {
  delimiter?: string;
  includeHeader?: boolean;
}

export interface ExcelSheetDefinition {
  name: string;
  records: any[];
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
export function readCsv(csvText: string, options?: CsvOptions): any[];
export function readFixedWidth(text: string, specs: ColumnSpec[]): any[];
export function readExcel(excelBytes: Uint8Array, sheetName?: string, hasHeader?: boolean): any[];
export function getExcelSheetNames(excelBytes: Uint8Array): string[];
export function detectCsvDelimiter(sampleRows: string): string;

export function writeCsv(records: any[], options?: CsvOptions): string;
export function writeFixedWidth(records: any[], specs: ColumnSpec[]): string;
export function convertCsvToFixedWidth(
  csvText: string,
  specs: ColumnSpec[],
  options?: FixedWidthConversionOptions,
): string;
export function convertFixedWidthToCsv(
  fixedWidthText: string,
  specs: ColumnSpec[],
  options?: FixedWidthConversionOptions,
): string;
export function writeExcel(records: any[], sheetName?: string, hasHeader?: boolean): Uint8Array;
export function writeExcelMultiSheet(
  sheets: ExcelSheetDefinition[],
  hasHeader?: boolean,
): Uint8Array;

export function validateCsv(csvText: string, options?: CsvValidationOptions): CsvValidationResult;
export function inferCsvSchema(
  csvText: string,
  options?: CsvSchemaInferenceOptions,
): CsvSchemaResult;
export function repairLlmCsv(text: string, options?: LlmRepairOptions): string;
export function convertCsvToJsonl(csvText: string, options?: CsvOptions): string;

export function readJsonl(jsonlText: string): any[];
export function writeJsonl(records: any[]): string;
export function convertJsonlToCsv(
  jsonlText: string,
  options?: Pick<CsvOptions, "delimiter">,
): string;

export function convertCsvToHtb(csvText: string, options?: HtbEncodeOptions): Uint8Array;
export function convertHtbToCsv(htbBytes: Uint8Array, options?: CsvOptions): string;
export function inspectHtb(htbBytes: Uint8Array): HtbInspectResult;
