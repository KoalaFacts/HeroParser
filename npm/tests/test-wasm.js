import zlib from "zlib";
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
  readJsonlAsJson,
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
  const gappedCsv = convertFixedWidthToCsv(
    "AB--CD\nEF--GH",
    [
      { name: "A", start: 0, length: 2 },
      { name: "B", start: 4, length: 2 },
    ],
    { includeHeader: true },
  );
  if (gappedCsv !== "A,B\r\nAB,CD\r\nEF,GH\r\n") {
    throw new Error(`Gapped fixed-width Start offset failed! Got ${JSON.stringify(gappedCsv)}`);
  }
  const gappedSpecs = [
    { name: "A", start: 0, length: 2 },
    { name: "B", start: 5, length: 2 },
  ];
  const gappedFromCsv = convertCsvToFixedWidth("A,B\n12,34\n56,78\n", gappedSpecs, {
    includeHeader: false,
  });
  if (gappedFromCsv !== "12   34\r\n56   78\r\n") {
    throw new Error(`convertCsvToFixedWidth gapped Start failed! Got ${JSON.stringify(gappedFromCsv)}`);
  }
  const roundTripGappedCsv = convertFixedWidthToCsv(gappedFromCsv, gappedSpecs, {
    includeHeader: true,
  });
  if (roundTripGappedCsv !== "A,B\r\n12,34\r\n56,78\r\n") {
    throw new Error(`Gapped round-trip failed! Got ${JSON.stringify(roundTripGappedCsv)}`);
  }
  const gappedWritten = writeFixedWidth([{ A: "hi", B: "yo" }], gappedSpecs);
  if (gappedWritten !== "hi   yo\r\n") {
    throw new Error(`writeFixedWidth gapped Start failed! Got ${JSON.stringify(gappedWritten)}`);
  }
  const overlappingSpecs = [
    { name: "A", start: 0, length: 4 },
    { name: "B", start: 2, length: 3 },
  ];
  for (const { label, fn } of [
    {
      label: "writeFixedWidth overlapping specs",
      fn: () => writeFixedWidth([{ A: "ab", B: "cd" }], overlappingSpecs),
    },
    {
      label: "convertCsvToFixedWidth overlapping specs",
      fn: () => convertCsvToFixedWidth("A,B\nab,cd\n", overlappingSpecs),
    },
  ]) {
    let overlapThrew = false;
    try {
      fn();
    } catch {
      overlapThrew = true;
    }
    if (!overlapThrew) {
      throw new Error(`Expected ${label} to reject overlapping start/length ranges!`);
    }
  }
  const headerInjectionCsv = convertFixedWidthToCsv(
    "ab\n",
    [{ name: "=cmd", start: 0, length: 2 }],
    { includeHeader: true },
  );
  if (!headerInjectionCsv.startsWith("\"'=cmd\"") && !headerInjectionCsv.startsWith("'=cmd")) {
    throw new Error(`Fixed-width header injection protection failed! Got ${JSON.stringify(headerInjectionCsv)}`);
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
  const precisionXlsx = writeExcel([
    {
      Exact15: 999999999999999,
      Over15: 1234567890123456,
      ExactDec15: 1.23456789012345,
      OverDec15: 1.234567890123456,
    },
  ]);
  const precisionRows = readExcel(precisionXlsx);
  const precisionSheetXml = readZipEntryText(precisionXlsx, "xl/worksheets/sheet1.xml");
  if (
    precisionRows.length !== 1 ||
    precisionRows[0].Exact15 !== "999999999999999" ||
    precisionRows[0].Over15 !== "1234567890123456" ||
    precisionRows[0].ExactDec15 !== "1.23456789012345" ||
    precisionRows[0].OverDec15 !== "1.234567890123456" ||
    !precisionSheetXml.includes('<c r="A2" ><v>999999999999999</v></c>') ||
    precisionSheetXml.includes('<c r="B2" ><v>') ||
    !precisionSheetXml.includes('<c r="C2" ><v>1.23456789012345</v></c>') ||
    precisionSheetXml.includes('<c r="D2" ><v>')
  ) {
    throw new Error(
      `WASM Excel 15-digit precision check failed! Rows=${JSON.stringify(precisionRows)}, Xml=${precisionSheetXml}`,
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
  const multilineJsonl = convertCsvToJsonl('Id,Note\r\n1,"line1\nline2"\r\n', {
    allowNewlinesInsideQuotes: true,
  });
  const multilineParsed = JSON.parse(multilineJsonl.trim());
  if (multilineParsed.Id !== "1" || multilineParsed.Note !== "line1\nline2") {
    throw new Error(
      `CSV to JSONL multiline option failed! Got ${JSON.stringify(multilineJsonl)}`,
    );
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
  const losslessLineOut = writeJsonl('{"id":9007199254740993}');
  const singleArrayLineOut = writeJsonl("[1, 2, 3]");
  const expandedArrayOut = writeJsonl("[1, 2, 3]", "json-array");
  const losslessArrayOut = writeJsonl('[{"id":9007199254740993}]', "json-array");
  const multiLineArrayJsonl = writeJsonl("[1,2]\n[3,4]");
  if (
    losslessLineOut.trim() !== '{"id":9007199254740993}' ||
    singleArrayLineOut.trim() !== "[1,2,3]" ||
    expandedArrayOut.trim().replace(/\r\n/g, "\n") !== "1\n2\n3" ||
    losslessArrayOut.trim() !== '{"id":9007199254740993}' ||
    multiLineArrayJsonl.trim().replace(/\r\n/g, "\n") !== "[1,2]\n[3,4]"
  ) {
    throw new Error(
      `Lossless writeJsonl rewrite failed! Got line=${JSON.stringify(losslessLineOut)}, singleArray=${JSON.stringify(singleArrayLineOut)}, expandedArray=${JSON.stringify(expandedArrayOut)}, losslessArray=${JSON.stringify(losslessArrayOut)}, multi=${JSON.stringify(multiLineArrayJsonl)}`,
    );
  }
  let threwInvalidFormat = false;
  try {
    writeJsonl("{}", "invalid");
  } catch {
    threwInvalidFormat = true;
  }
  let threwNonArray = false;
  try {
    writeJsonl('{"a":1}', "json-array");
  } catch {
    threwNonArray = true;
  }
  if (!threwInvalidFormat || !threwNonArray) {
    throw new Error(
      `Expected writeJsonl validation errors (invalidFormat=${threwInvalidFormat}, nonArray=${threwNonArray})`,
    );
  }
  const multilineFieldJsonl = '{"text":"one\\ntwo"}';
  const multilineFieldCsv = convertJsonlToCsv(multilineFieldJsonl);
  const multilineFieldRecords = readJsonl(multilineFieldJsonl);
  if (multilineFieldRecords.length !== 1 || !multilineFieldCsv.includes('"one\ntwo"')) {
    throw new Error(
      `Multiline JSONL record count check failed! Records=${JSON.stringify(multilineFieldRecords)}, Csv=${JSON.stringify(multilineFieldCsv)}`,
    );
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

  const trickyHtbBytes = convertCsvToHtb('Id,Id,,Note\r\n1,2,mid,"line1\nline2"\r\n');
  const trickyHtbInfo = inspectHtb(trickyHtbBytes);
  const trickyDecoded = convertHtbToCsv(trickyHtbBytes);
  const trickyParsed = readCsv(trickyDecoded, {
    hasHeader: false,
    allowNewlinesInsideQuotes: true,
  });
  if (
    trickyDecoded !== 'Id,Id,Col3,Note\r\n1,2,mid,"line1\nline2"\r\n' ||
    trickyHtbInfo.columns[2].name !== "Col3" ||
    trickyParsed.length !== 2 ||
    trickyParsed[1][0] !== "1" ||
    trickyParsed[1][1] !== "2" ||
    trickyParsed[1][2] !== "mid" ||
    trickyParsed[1][3] !== "line1\nline2"
  ) {
    throw new Error(
      `Tricky CSV -> HTB -> CSV failed! Schema=${JSON.stringify(trickyHtbInfo)}, Csv=${JSON.stringify(trickyDecoded)}, Rows=${JSON.stringify(trickyParsed)}`,
    );
  }
  let extraColThrew = false;
  try {
    convertCsvToHtb("Id,Name\r\n1,Alice,Extra\r\n");
  } catch {
    extraColThrew = true;
  }
  if (!extraColThrew) {
    throw new Error("Expected convertCsvToHtb to reject rows wider than inferred schema!");
  }

  const losslessHtbBytes = convertCsvToHtb(
    "Zip,Signed,Precise,Amount\n00123,+42,1.000000000000000000000000000001,2.50\n",
  );
  const losslessHtbInfo = inspectHtb(losslessHtbBytes);
  const losslessRows = readCsv(convertHtbToCsv(losslessHtbBytes));
  if (
    losslessHtbInfo.columns[0].dataType !== "String" ||
    losslessHtbInfo.columns[1].dataType !== "String" ||
    losslessHtbInfo.columns[2].dataType !== "String" ||
    losslessHtbInfo.columns[3].dataType !== "Decimal" ||
    losslessRows.length !== 1 ||
    losslessRows[0].Zip !== "00123" ||
    losslessRows[0].Signed !== "+42" ||
    losslessRows[0].Precise !== "1.000000000000000000000000000001" ||
    losslessRows[0].Amount !== "2.50"
  ) {
    throw new Error(
      `Lossless CSV -> HTB -> CSV failed! Info=${JSON.stringify(losslessHtbInfo)}, Rows=${JSON.stringify(losslessRows)}`,
    );
  }

  const wideHeaders = Array.from({ length: 105 }, (_, idx) => `Col${idx + 1}`);
  const wideValues = Array.from({ length: 105 }, (_, idx) => `${idx + 1}`);
  const wideHeaderHtb = convertCsvToHtb(`${wideHeaders.join(",")}\n${wideValues.join(",")}\n`);
  const wideHeaderInfo = inspectHtb(wideHeaderHtb);
  const wideHeaderCsv = convertHtbToCsv(wideHeaderHtb).trim().replace(/\r\n/g, "\n");
  if (
    wideHeaderInfo.columns.length !== 105 ||
    wideHeaderInfo.columns[104].name !== "Col105" ||
    wideHeaderCsv !== `${wideHeaders.join(",")}\n${wideValues.join(",")}`
  ) {
    throw new Error(
      `105-column header CSV -> HTB failed! Cols=${wideHeaderInfo.columns.length}`,
    );
  }
  const wideNoHeaderHtb = convertCsvToHtb(`${wideValues.join(",")}\n`, { hasHeader: false });
  const wideNoHeaderInfo = inspectHtb(wideNoHeaderHtb);
  const wideNoHeaderCsv = convertHtbToCsv(wideNoHeaderHtb, { hasHeader: false }).trim().replace(/\r\n/g, "\n");
  if (
    wideNoHeaderInfo.columns.length !== 105 ||
    wideNoHeaderInfo.recordCount !== 1 ||
    wideNoHeaderCsv !== wideValues.join(",")
  ) {
    throw new Error(
      `105-column headerless CSV -> HTB failed! Cols=${wideNoHeaderInfo.columns.length}`,
    );
  }

  // Default omitted alignment/padChar skips whitespace-only lines and trims all whitespace (including tabs);
  // explicit alignment/padChar options preserve whitespace-only rows and non-matching whitespace.
  const trimmedFw = readFixedWidth(
    "\t42\t  \tAlice\t\n   \n\t  \n  7     Bob   \n",
    [
      { name: "Id", start: 0, length: 6 },
      { name: "Name", start: 6, length: 8 },
    ],
  );
  if (
    trimmedFw.length !== 2 ||
    trimmedFw[0].Id !== "42" ||
    trimmedFw[0].Name !== "Alice" ||
    trimmedFw[1].Id !== "7" ||
    trimmedFw[1].Name !== "Bob"
  ) {
    throw new Error(`Fixed-width default trim/whitespace skip failed! Got ${JSON.stringify(trimmedFw)}`);
  }

  const convertedTrimmedCsv = convertFixedWidthToCsv(
    "\t42\t  \tAlice\t\n   \n\t  \n  7     Bob   \n",
    [
      { name: "Id", start: 0, length: 6 },
      { name: "Name", start: 6, length: 8 },
    ],
    { includeHeader: true },
  );
  if (convertedTrimmedCsv !== "Id,Name\r\n42,Alice\r\n7,Bob\r\n") {
    throw new Error(`Fixed-width -> CSV default trim/whitespace skip failed! Got ${JSON.stringify(convertedTrimmedCsv)}`);
  }

  const explicitNoneFw = readFixedWidth(
    "  42   Alice  \n\n              \n",
    [
      { name: "Id", start: 0, length: 6, alignment: "none" },
      { name: "Name", start: 6, length: 8, alignment: "none" },
    ],
  );
  if (
    explicitNoneFw.length !== 2 ||
    explicitNoneFw[0].Id !== "  42  " ||
    explicitNoneFw[0].Name !== " Alice  " ||
    explicitNoneFw[1].Id !== "      " ||
    explicitNoneFw[1].Name !== "        "
  ) {
    throw new Error(`Fixed-width explicit alignment:none failed! Got ${JSON.stringify(explicitNoneFw)}`);
  }

  const preservedWhitespaceCsv = convertFixedWidthToCsv(
    "   ",
    [{ name: "Code", start: 0, length: 3, alignment: "none" }],
    { includeHeader: true },
  );
  if (preservedWhitespaceCsv !== "Code\r\n   \r\n") {
    throw new Error(
      `Fixed-width whitespace-only record preservation failed! Got ${JSON.stringify(preservedWhitespaceCsv)}`,
    );
  }
  const skippedWhitespaceCsv = convertFixedWidthToCsv(
    "   ",
    [{ name: "Code", start: 0, length: 3 }],
    { includeHeader: true },
  );
  if (skippedWhitespaceCsv !== "") {
    throw new Error(
      `Fixed-width default whitespace-only skip failed! Got ${JSON.stringify(skippedWhitespaceCsv)}`,
    );
  }

  const exactBigIntExcel = writeExcel('[{"id":9007199254740993}]');
  const exactBigIntRows = readExcel(exactBigIntExcel);
  if (exactBigIntRows.length !== 1 || exactBigIntRows[0].id !== "9007199254740993") {
    throw new Error(`Raw JSON writeExcel precision failed! Got ${JSON.stringify(exactBigIntRows)}`);
  }

  const exactBigIntMultiExcel = writeExcelMultiSheet([
    { name: "Big", records: '[{"id":9007199254740993}]' },
  ]);
  const exactBigIntMultiRows = readExcel(exactBigIntMultiExcel, "Big", true);
  if (exactBigIntMultiRows.length !== 1 || exactBigIntMultiRows[0].id !== "9007199254740993") {
    throw new Error(
      `Raw JSON writeExcelMultiSheet precision failed! Got ${JSON.stringify(exactBigIntMultiRows)}`,
    );
  }

  const reorderedMultiExcel = writeExcelMultiSheet([
    { name: "Sheet2", records: [{ a: 1 }] },
    { name: "", records: [{ b: 2 }] },
    { records: [{ c: 3 }] },
  ]);
  const reorderedSheetNames = getExcelSheetNames(reorderedMultiExcel);
  if (JSON.stringify(reorderedSheetNames) !== JSON.stringify(["Sheet2", "Sheet1", "Sheet3"])) {
    throw new Error(
      `Unexpected fallback sheet names when Sheet2 precedes blank names: ${JSON.stringify(reorderedSheetNames)}`,
    );
  }

  const dupHeaderXlsx = writeExcel(
    [
      ["Price", "Price", ""],
      ["10", "20", "30"],
    ],
    "Sheet1",
    false,
  );
  const positionalExcelRows = readExcel(dupHeaderXlsx, "", false);
  const positionalExcelCsv = writeCsv(positionalExcelRows, { delimiter: ",", hasHeader: false });
  if (positionalExcelCsv !== "Price,Price,\r\n10,20,30\r\n") {
    throw new Error(
      `Positional Excel-to-CSV export failed! Got ${JSON.stringify(positionalExcelCsv)}`,
    );
  }

  const exactJsonlPreview = readJsonlAsJson('{"id":9007199254740993}\n', { indented: true });
  if (
    !exactJsonlPreview.includes('"id": 9007199254740993') ||
    exactJsonlPreview.includes("9007199254740992")
  ) {
    throw new Error(`Lossless JSONL preview failed! Got ${exactJsonlPreview}`);
  }

  // Uniform row shape validation in writers
  const expectThrows = (label, fn) => {
    let threw = false;
    try {
      fn();
    } catch {
      threw = true;
    }
    if (!threw) {
      throw new Error(`Expected ${label} to throw!`);
    }
  };
  expectThrows("writeCsv mixed object+array", () => writeCsv([{ a: 1 }, [2]]));
  expectThrows("writeCsv mixed array+object", () => writeCsv([[1], { a: 2 }]));
  expectThrows("writeCsv empty objects", () => writeCsv([{}, {}]));
  expectThrows("writeCsv primitive elements", () => writeCsv([1, 2]));
  expectThrows("writeExcel mixed object+array", () => writeExcel([{ a: 1 }, [2]]));
  expectThrows("writeExcel empty objects", () => writeExcel([{}]));
  expectThrows("writeFixedWidth non-object rows", () =>
    writeFixedWidth([[1]], [{ name: "A", start: 0, length: 4 }]),
  );
  expectThrows("writeFixedWidth mixed object+array", () =>
    writeFixedWidth([{ Name: "Alice" }, [2]], [{ name: "Name", start: 0, length: 8 }]),
  );
  expectThrows("writeFixedWidth newline padChar", () =>
    writeFixedWidth(
      [{ A: "1", B: "2" }],
      [
        { name: "A", length: 4, padChar: "\n" },
        { name: "B", length: 4 },
      ],
    ),
  );
  expectThrows("writeFixedWidth carriage return padChar", () =>
    writeFixedWidth(
      [{ A: "1", B: "2" }],
      [
        { name: "A", length: 4, padChar: "\r" },
        { name: "B", length: 4 },
      ],
    ),
  );

  // Truncated 20k-byte HTB string must fail inspectHtb rather than seeking past EOF
  const largeHtbBytes = convertCsvToHtb(`Id,Bio\n1,${"x".repeat(20000)}\n`);
  expectThrows("inspectHtb truncated payload", () =>
    inspectHtb(largeHtbBytes.slice(0, largeHtbBytes.length - 5000)),
  );

  // Single-column CSV with omitted delimiter should convert cleanly to HTB and back,
  // while genuinely ambiguous multi-column input still rejects omitted delimiter.
  const singleColHtb = convertCsvToHtb("Name\nAlice\n");
  const singleColCsv = convertHtbToCsv(singleColHtb);
  if (singleColCsv !== "Name\r\nAlice\r\n") {
    throw new Error(`Single-column convertCsvToHtb failed! Got ${JSON.stringify(singleColCsv)}`);
  }
  expectThrows("ambiguous multi-column convertCsvToHtb", () => convertCsvToHtb("a,b\n1;2\n"));

  // Empty string vs null preservation in CSV -> HTB -> CSV
  const emptyVsNullHtb = convertCsvToHtb('id,note\r\n1,""\r\n2,\r\n');
  const emptyVsNullCsv = convertHtbToCsv(emptyVsNullHtb);
  if (emptyVsNullCsv !== 'id,note\r\n1,""\r\n2,\r\n') {
    throw new Error(`Expected empty string vs null preservation, got ${JSON.stringify(emptyVsNullCsv)}`);
  }

  // Fixed-width default alignment with padChar must trim only the padded side (right side for default Left alignment),
  // while specs with neither alignment nor padChar retain legacy bidirectional whitespace trimming.
  const padOnlyCols = [{ name: "code", length: 5, padChar: "0" }];
  const padOnlyFw = writeFixedWidth([{ code: "012" }], padOnlyCols);
  if (padOnlyFw.replace(/\r\n/g, "") !== "01200") {
    throw new Error(`Expected "01200" from padOnly fixed-width write, got ${JSON.stringify(padOnlyFw)}`);
  }
  const padOnlyParsed = readFixedWidth(padOnlyFw, padOnlyCols);
  if (padOnlyParsed.length !== 1 || padOnlyParsed[0].code !== "012") {
    throw new Error(`Expected "012" from padOnly fixed-width read, got ${JSON.stringify(padOnlyParsed)}`);
  }
  const legacyTrimParsed = readFixedWidth("  hi \r\n", [{ name: "code", length: 5 }]);
  if (legacyTrimParsed.length !== 1 || legacyTrimParsed[0].code !== "hi") {
    throw new Error(`Expected legacy whitespace trim to yield "hi", got ${JSON.stringify(legacyTrimParsed)}`);
  }
  console.log("✓ HTB binary encode/inspect/decode passed!");

  console.log("\n--------------------------------------------------");
  console.log("✓ ALL WASM INTEGRATION TESTS PASSED!");
  console.log("--------------------------------------------------");
}

function readZipEntryText(zipBytes, targetName) {
  const buf = Buffer.from(zipBytes);
  let eocdOffset = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 65558); i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) {
      eocdOffset = i;
      break;
    }
  }
  if (eocdOffset < 0) {
    throw new Error("Invalid ZIP: EOCD not found");
  }
  const cdSize = buf.readUInt32LE(eocdOffset + 12);
  let cdOffset = buf.readUInt32LE(eocdOffset + 16);
  const cdEnd = cdOffset + cdSize;
  while (cdOffset < cdEnd) {
    if (buf.readUInt32LE(cdOffset) !== 0x02014b50) {
      throw new Error("Invalid ZIP central directory entry");
    }
    const method = buf.readUInt16LE(cdOffset + 10);
    const compSize = buf.readUInt32LE(cdOffset + 20);
    const nameLen = buf.readUInt16LE(cdOffset + 28);
    const extraLen = buf.readUInt16LE(cdOffset + 30);
    const commentLen = buf.readUInt16LE(cdOffset + 32);
    const localHeaderOffset = buf.readUInt32LE(cdOffset + 42);
    const name = buf.subarray(cdOffset + 46, cdOffset + 46 + nameLen).toString("utf8");
    if (name === targetName) {
      const lfhNameLen = buf.readUInt16LE(localHeaderOffset + 26);
      const lfhExtraLen = buf.readUInt16LE(localHeaderOffset + 28);
      const dataStart = localHeaderOffset + 30 + lfhNameLen + lfhExtraLen;
      const compressed = buf.subarray(dataStart, dataStart + compSize);
      const raw = method === 0 ? compressed : zlib.inflateRawSync(compressed);
      return raw.toString("utf8");
    }
    cdOffset += 46 + nameLen + extraLen + commentLen;
  }
  throw new Error(`ZIP entry not found: ${targetName}`);
}

run().catch((err) => {
  console.error("\n❌ WASM Integration Tests Failed:", err);
  process.exit(1);
});
