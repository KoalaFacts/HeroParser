import {
  init,
  readCsv,
  detectCsvDelimiter,
  readFixedWidth,
  readExcel,
  writeCsv,
  writeFixedWidth,
  writeExcel,
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
  const fwText = "Alice     30        Developer \nBob       25        Designer  ";
  const specs = [
    { name: "Name", start: 0, length: 10 },
    { name: "Age", start: 10, length: 10 },
    { name: "Role", start: 20, length: 11 },
  ];
  const fwResult = readFixedWidth(fwText, specs);
  console.log(fwResult);
  if (fwResult.length !== 2 || fwResult[0].Name !== "Alice" || fwResult[1].Role !== "Designer") {
    throw new Error("Fixed-Width parsing failed!");
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

  // 5. CSV Write Test: all buffered output is returned
  console.log("\n[Test 5] Writing CSV...");
  const people = [
    { Name: "Alice", Age: "30" },
    { Name: "Bob, Jr.", Age: "25" },
  ];
  const written = writeCsv(people);
  console.log(JSON.stringify(written));
  const writtenLines = written.split(/\r?\n/).filter((line) => line.length > 0);
  if (
    writtenLines.length !== 3 ||
    writtenLines[0] !== "Name,Age" ||
    writtenLines[2] !== '"Bob, Jr.",25'
  ) {
    throw new Error(`CSV writing failed! Got ${JSON.stringify(written)}`);
  }
  const many = Array.from({ length: 5000 }, (_, i) => ({ Id: String(i), Text: `row ${i}` }));
  const manyLines = writeCsv(many)
    .split(/\r?\n/)
    .filter((line) => line.length > 0);
  if (manyLines.length !== 5001 || manyLines[5000] !== "4999,row 4999") {
    throw new Error(`CSV writing truncated output: ${manyLines.length} of 5001 lines`);
  }
  // Values without quotes keep this check independent of readCsv quote handling.
  const roundTrip = readCsv(writeCsv(many.slice(0, 3)));
  if (roundTrip.length !== 3 || roundTrip[2].Text !== "row 2") {
    throw new Error("CSV write/read round trip failed!");
  }
  console.log("✓ CSV writing passed!");

  // 6. Fixed-Width Write Test
  console.log("\n[Test 6] Writing Fixed-Width text...");
  const fwWritten = writeFixedWidth(fwResult, specs);
  console.log(JSON.stringify(fwWritten));
  const fwLines = fwWritten.split(/\r?\n/).filter((line) => line.length > 0);
  if (fwLines.length !== 2 || !fwLines[0].startsWith("Alice     30        Developer")) {
    throw new Error(`Fixed-Width writing failed! Got ${JSON.stringify(fwWritten)}`);
  }
  console.log("✓ Fixed-Width writing passed!");

  // 7. Excel round trip: header row and every data row survive
  console.log("\n[Test 7] Excel write/read round trip...");
  const xlsx = writeExcel(people);
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
  console.log("✓ Excel round trip passed!");

  console.log("\n--------------------------------------------------");
  console.log("✓ ALL WASM INTEGRATION TESTS PASSED!");
  console.log("--------------------------------------------------");
}

run().catch((err) => {
  console.error("\n❌ WASM Integration Tests Failed:", err);
  process.exit(1);
});
