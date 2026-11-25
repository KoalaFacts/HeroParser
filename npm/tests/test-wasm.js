import {
  init,
  readCsv,
  detectCsvDelimiter,
  readFixedWidth,
  readExcel,
  getExcelSheetNames,
  writeCsv,
  writeFixedWidth,
  convertCsvToFixedWidth,
  convertFixedWidthToCsv,
  writeExcel,
  writeExcelMultiSheet,
  validateCsv,
  inferCsvSchema,
  repairLlmCsv,
  convertCsvToJsonl,
  readJsonl,
  writeJsonl,
  convertJsonlToCsv,
  convertCsvToHtb,
  convertHtbToCsv,
  inspectHtb,
} from "heroparser";

async function run() {
  console.log("--------------------------------------------------");
  console.log("Starting HeroParser WASM Integration Tests...");
  console.log("--------------------------------------------------");

  console.log("Initializing WASM runtime...");
  await init();
  console.log("Successfully initialized!");

  // 1. CSV Parse Test (Header)
  console.log("\n[Test 1] Parsing CSV with headers...");
  const csv = "Name,Age,Role\nAlice,30,Developer\nBob,25,Designer";
  const csvResult = readCsv(csv);
  console.log(csvResult);
  if (csvResult.length !== 2 || csvResult[0].Name !== "Alice" || csvResult[1].Role !== "Designer") {
    throw new Error("CSV parsing with headers failed!");
  }
  console.log("✓ CSV parsing with headers passed!");

  // 2. CSV Parse Test (No Header)
  console.log("\n[Test 2] Parsing CSV without headers...");
  const csvNoHeader = "Alice,30,Developer\nBob,25,Designer";
  const csvNoHeaderResult = readCsv(csvNoHeader, { hasHeader: false });
  console.log(csvNoHeaderResult);
  if (
    csvNoHeaderResult.length !== 2 ||
    csvNoHeaderResult[0][0] !== "Alice" ||
    csvNoHeaderResult[1][2] !== "Designer"
  ) {
    throw new Error("CSV parsing without headers failed!");
  }
  console.log("✓ CSV parsing without headers passed!");

  // 2b. Quoted CSV (header names and values are unquoted)
  console.log("\n[Test 2b] Parsing quoted CSV...");
  const quotedCsv = '"Id","Name","Note"\n1,"Smith, ""J""","x"\n2,Bob,';
  const quotedResult = readCsv(quotedCsv);
  console.log(quotedResult);
  if (
    quotedResult.length !== 2 ||
    quotedResult[0].Id !== "1" ||
    quotedResult[0].Name !== 'Smith, "J"' ||
    quotedResult[1].Name !== "Bob" ||
    quotedResult[1].Note !== ""
  ) {
    throw new Error("Quoted CSV parsing with headers failed!");
  }
  const quotedNoHeader = readCsv('"Smith, ""J""",2', { hasHeader: false });
  console.log(quotedNoHeader);
  if (
    quotedNoHeader.length !== 1 ||
    quotedNoHeader[0][0] !== 'Smith, "J"' ||
    quotedNoHeader[0][1] !== "2"
  ) {
    throw new Error("Quoted CSV parsing without headers failed!");
  }
  console.log("✓ Quoted CSV parsing passed!");

  // 3. Fixed-Width Parse Test
  console.log("\n[Test 3] Parsing Fixed-Width text...");
  const fwText = "Alice     00030Developer \nBob       00025Designer  ";
  const specs = [
    { name: "Name", start: 0, length: 10, alignment: "left", padChar: " " },
    { name: "Age", start: 10, length: 5, alignment: "right", padChar: "0" },
    { name: "Role", start: 15, length: 11, alignment: "left", padChar: " " },
  ];
  const fwResult = readFixedWidth(fwText, specs);
  console.log(fwResult);
  if (
    fwResult.length !== 2 ||
    fwResult[0].Name !== "Alice" ||
    fwResult[0].Age !== "30" ||
    fwResult[1].Role !== "Designer"
  ) {
    throw new Error("Fixed-Width parsing with alignment/padChar failed!");
  }
  console.log("✓ Fixed-Width parsing passed!");

  // 4. Delimiter Detection Test
  console.log("\n[Test 4] Detecting CSV Delimiter...");
  const semiSample = "Name;Age;Role\nAlice;30;Developer\nBob;25;Designer";
  const delim = detectCsvDelimiter(semiSample);
  console.log(`Detected: "${delim}"`);
  if (delim !== ";") {
    throw new Error(`Expected delimiter ";" but got "${delim}"`);
  }
  console.log("✓ Delimiter detection passed!");

  // 5. CSV Write Test: all buffered output + mixed primitive types
  console.log("\n[Test 5] Writing CSV...");
  const people = [
    { Name: "Alice", Age: 30, Active: true },
    { Name: "Bob, Jr.", Age: 25, Active: false },
  ];
  const written = writeCsv(people);
  console.log(JSON.stringify(written));
  const writtenLines = written.split(/\r?\n/).filter((line) => line.length > 0);
  if (
    writtenLines.length !== 3 ||
    writtenLines[0] !== "Name,Age,Active" ||
    writtenLines[1] !== "Alice,30,true" ||
    writtenLines[2] !== '"Bob, Jr.",25,false'
  ) {
    throw new Error(`CSV writing failed! Got ${JSON.stringify(written)}`);
  }
  const arrayCsv = writeCsv([
    ["Alice", 30],
    ["Bob", 25],
  ]);
  if (!arrayCsv.includes("Alice,30") || !arrayCsv.includes("Bob,25")) {
    throw new Error(`CSV array writing failed! Got ${JSON.stringify(arrayCsv)}`);
  }
  const many = Array.from({ length: 5000 }, (_, i) => ({ Id: i, Text: `row ${i}` }));
  const manyLines = writeCsv(many)
    .split(/\r?\n/)
    .filter((line) => line.length > 0);
  if (manyLines.length !== 5001 || manyLines[5000] !== "4999,row 4999") {
    throw new Error(`CSV writing truncated output: ${manyLines.length} of 5001 lines`);
  }
  const roundTrip = readCsv(writeCsv(many.slice(0, 3)));
  if (roundTrip.length !== 3 || roundTrip[2].Text !== "row 2") {
    throw new Error("CSV write/read round trip failed!");
  }
  console.log("✓ CSV writing passed!");

  // 6. Fixed-Width Write & Conversion Tests
  console.log("\n[Test 6] Writing & Converting Fixed-Width text...");
  const fwWritten = writeFixedWidth(
    [
      { Name: "Alice", Age: 30, Role: "Developer" },
      { Name: "Bob", Age: 25, Role: "Designer" },
    ],
    specs,
  );
  console.log(JSON.stringify(fwWritten));
  const fwLines = fwWritten.split(/\r?\n/).filter((line) => line.length > 0);
  if (fwLines.length !== 2 || !fwLines[0].startsWith("Alice     00030Developer")) {
    throw new Error(`Fixed-Width writing failed! Got ${JSON.stringify(fwWritten)}`);
  }
  const fwToCsv = convertFixedWidthToCsv(fwWritten, specs, { includeHeader: true });
  const csvToFw = convertCsvToFixedWidth(fwToCsv, specs, { includeHeader: false });
  const fwRoundTrip = readFixedWidth(csvToFw, specs);
  if (fwRoundTrip.length !== 2 || fwRoundTrip[0].Age !== "30" || fwRoundTrip[1].Name !== "Bob") {
    throw new Error(`Fixed-Width <-> CSV round trip failed! Got ${JSON.stringify(fwRoundTrip)}`);
  }
  console.log("✓ Fixed-Width writing and conversion passed!");

  // 7. Excel round trip (single + multi-sheet + sheet names)
  console.log("\n[Test 7] Excel write/read & multi-sheet round trip...");
  const xlsx = writeExcel(people);
  const singleSheetNames = getExcelSheetNames(xlsx);
  if (singleSheetNames.length !== 1 || singleSheetNames[0] !== "Sheet1") {
    throw new Error(`Expected ["Sheet1"], got ${JSON.stringify(singleSheetNames)}`);
  }
  const sheetRows = readExcel(xlsx);
  console.log(sheetRows);
  if (
    sheetRows.length !== 2 ||
    sheetRows[0].Name !== "Alice" ||
    sheetRows[1].Name !== "Bob, Jr." ||
    sheetRows[1].Age !== "25"
  ) {
    throw new Error(`Excel round trip with header failed! Got ${JSON.stringify(sheetRows)}`);
  }
  const rawRows = readExcel(xlsx, "", false);
  if (rawRows.length !== 3 || rawRows[0][0] !== "Name" || rawRows[2][0] !== "Bob, Jr.") {
    throw new Error(`Excel read without header failed! Got ${JSON.stringify(rawRows)}`);
  }
  const multiXlsx = writeExcelMultiSheet([
    { name: "Sales", records: [{ Region: "North", Units: 120, Active: true }] },
    { name: "Team", records: [{ Name: "Alice", Role: "Developer" }] },
  ]);
  const multiNames = getExcelSheetNames(multiXlsx);
  const teamSheetRows = readExcel(multiXlsx, "Team", true);
  if (
    multiNames.length !== 2 ||
    multiNames[0] !== "Sales" ||
    multiNames[1] !== "Team" ||
    teamSheetRows.length !== 1 ||
    teamSheetRows[0].Name !== "Alice"
  ) {
    throw new Error(
      `Multi-sheet Excel failed! Names=${JSON.stringify(multiNames)}, Team=${JSON.stringify(teamSheetRows)}`,
    );
  }
  console.log("✓ Excel single-sheet and multi-sheet round trip passed!");

  // 8. CSV Validation Test
  console.log("\n[Test 8] Validating CSV...");
  const validRes = validateCsv("Name,Age\nAlice,30\nBob,25");
  if (!validRes.isValid || validRes.totalRows !== 3 || validRes.columnCount !== 2) {
    throw new Error(`Expected valid CSV result, got ${JSON.stringify(validRes)}`);
  }
  const semiRes = validateCsv("Name;Age\nAlice;30\nBob;25");
  if (!semiRes.isValid || semiRes.delimiter !== ";" || semiRes.columnCount !== 2) {
    throw new Error(`Expected semicolon auto-detection, got ${JSON.stringify(semiRes)}`);
  }
  const invalidRes = validateCsv("Name,Age\nAlice,30\nBob", { delimiter: "," });
  if (
    invalidRes.isValid ||
    invalidRes.errors.length !== 1 ||
    invalidRes.errors[0].rowNumber !== 3
  ) {
    throw new Error(
      `Expected inconsistent column error on row 3, got ${JSON.stringify(invalidRes)}`,
    );
  }
  console.log("✓ CSV validation passed!");

  // 9. CSV Schema Inference Test (with DistinguishTemporalTypes)
  console.log("\n[Test 9] Inferring CSV schema...");
  const schemaRes = inferCsvSchema(
    "Id,Price,Active,CreatedDate,StartTime\n1,19.99,true,2026-01-01,09:30:00\n2,,false,2026-01-02,14:15:00",
    { distinguishTemporalTypes: true },
  );
  if (
    schemaRes.sampledRowCount !== 2 ||
    schemaRes.columns.length !== 5 ||
    schemaRes.columns[0].inferredType !== "Integer" ||
    schemaRes.columns[1].inferredType !== "Decimal" ||
    !schemaRes.columns[1].isNullable ||
    schemaRes.columns[2].inferredType !== "Boolean" ||
    schemaRes.columns[3].inferredType !== "DateOnly" ||
    schemaRes.columns[4].inferredType !== "TimeOnly"
  ) {
    throw new Error(`CSV schema inference failed! Got ${JSON.stringify(schemaRes)}`);
  }
  console.log("✓ CSV schema inference passed!");

  // 10. LLM CSV Repair Test
  console.log("\n[Test 10] Repairing LLM CSV output...");
  const rawLlm = '```csv\nName,Quote\nAlice,"Hello cutoff\n';
  const repaired = repairLlmCsv(rawLlm);
  if (!repaired.includes('Alice,"Hello cutoff"') || repaired.includes("```")) {
    throw new Error(`LLM CSV repair failed! Got ${JSON.stringify(repaired)}`);
  }
  console.log("✓ LLM CSV repair passed!");

  // 11. CSV <-> JSONL Tests
  console.log("\n[Test 11] Converting CSV <-> JSONL & reading/writing JSONL...");
  const jsonl = convertCsvToJsonl("Name,Age\nAlice,30\nBob,25");
  const jsonlLines = jsonl.split(/\r?\n/).filter((l) => l.length > 0);
  if (
    jsonlLines.length !== 2 ||
    JSON.parse(jsonlLines[0]).Name !== "Alice" ||
    JSON.parse(jsonlLines[1]).Age !== "25"
  ) {
    throw new Error(`CSV to JSONL conversion failed! Got ${JSON.stringify(jsonl)}`);
  }
  const parsedJsonl = readJsonl(
    '{"id":1,"name":"Alice","active":true}\n{"id":2,"name":"Bob","role":"Lead"}',
  );
  if (parsedJsonl.length !== 2 || parsedJsonl[0].id !== 1 || parsedJsonl[1].role !== "Lead") {
    throw new Error(`readJsonl failed! Got ${JSON.stringify(parsedJsonl)}`);
  }
  const writtenJsonl = writeJsonl(parsedJsonl);
  const jsonlAsCsv = convertJsonlToCsv(writtenJsonl);
  const parsedBackCsv = readCsv(jsonlAsCsv);
  if (
    parsedBackCsv.length !== 2 ||
    parsedBackCsv[0].active !== "true" ||
    parsedBackCsv[1].role !== "Lead"
  ) {
    throw new Error(`JSONL -> CSV round trip failed! Got ${JSON.stringify(parsedBackCsv)}`);
  }
  console.log("✓ JSONL read/write/convert tests passed!");

  // 12. CSV <-> HTB Binary Round Trip Tests
  console.log("\n[Test 12] Encoding CSV -> HTB, inspecting schema, and decoding HTB -> CSV...");
  const htbSourceCsv =
    "Id,Name,Score,Active,HireDate,CheckIn\n1,Alice,98.5,true,2024-03-15,14:30:45.789\n2,Bob,,false,2025-07-01,09:05:12";
  const htbBytes = convertCsvToHtb(htbSourceCsv, { distinguishTemporalTypes: true });
  if (!(htbBytes instanceof Uint8Array) || htbBytes.byteLength < 10) {
    throw new Error("convertCsvToHtb returned empty payload!");
  }
  const htbInfo = inspectHtb(htbBytes);
  console.log(htbInfo);
  if (
    htbInfo.recordCount !== 2 ||
    htbInfo.columns.length !== 6 ||
    htbInfo.columns[0].dataType !== "Int32" ||
    htbInfo.columns[2].dataType !== "Decimal" ||
    htbInfo.columns[3].dataType !== "Boolean" ||
    htbInfo.columns[4].dataType !== "DateOnly" ||
    htbInfo.columns[5].dataType !== "TimeOnly"
  ) {
    throw new Error(`HTB inspection failed! Got ${JSON.stringify(htbInfo)}`);
  }
  const decodedCsv = convertHtbToCsv(htbBytes);
  const decodedRows = readCsv(decodedCsv);
  if (
    decodedRows.length !== 2 ||
    decodedRows[0].Name !== "Alice" ||
    decodedRows[1].Score !== "" ||
    decodedRows[0].HireDate !== "2024-03-15" ||
    !decodedRows[0].CheckIn.startsWith("14:30:45.789")
  ) {
    throw new Error(`HTB -> CSV round trip failed! Got ${JSON.stringify(decodedRows)}`);
  }
  console.log("✓ HTB binary encode/inspect/decode passed!");

  console.log("\n--------------------------------------------------");
  console.log("✓ ALL WASM INTEGRATION TESTS PASSED!");
  console.log("--------------------------------------------------");
}

run().catch((err) => {
  console.error("\n❌ WASM Integration Tests Failed:", err);
  process.exit(1);
});
