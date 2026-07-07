export interface CsvOptions {
    delimiter?: string;
    hasHeader?: boolean;
}

export interface ColumnSpec {
    name: string;
    start: number;
    length: number;
}

export function init(): Promise<void>;
export function readCsv(csvText: string, options?: CsvOptions): any[];
export function readFixedWidth(text: string, specs: ColumnSpec[]): any[];
export function readExcel(excelBytes: Uint8Array, sheetName?: string, hasHeader?: boolean): any[];
export function detectCsvDelimiter(sampleRows: string): string;

export function writeCsv(records: any[], options?: CsvOptions): string;
export function writeFixedWidth(records: any[], specs: ColumnSpec[]): string;
export function writeExcel(records: any[], sheetName?: string, hasHeader?: boolean): Uint8Array;
