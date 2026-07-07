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
export function parseCsv(csvText: string, options?: CsvOptions): any[];
export function parseFixedWidth(text: string, specs: ColumnSpec[]): any[];
export function parseExcel(excelBytes: Uint8Array, sheetName?: string, hasHeader?: boolean): any[];
export function detectCsvDelimiter(sampleRows: string): string;
export function repairTabularOutput(rawText: string): string;

export function writeCsv(records: any[], options?: CsvOptions): string;
export function writeFixedWidth(records: any[], specs: ColumnSpec[]): string;
export function writeExcel(records: any[], sheetName?: string, hasHeader?: boolean): Uint8Array;

export const readCsv: typeof parseCsv;
export const readFixedWidth: typeof parseFixedWidth;
export const readExcel: typeof parseExcel;
