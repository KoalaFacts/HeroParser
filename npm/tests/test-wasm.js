import { init, parseCsv, detectCsvDelimiter, repairTabularOutput, parseFixedWidth } from 'heroparser';

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
    const csvResult = parseCsv(csv);
    console.log(csvResult);
    if (csvResult.length !== 2 || csvResult[0].Name !== "Alice" || csvResult[1].Role !== "Designer") {
        throw new Error("CSV parsing with headers failed!");
    }
    console.log("✓ CSV parsing with headers passed!");

    // 2. CSV Parse Test (No Header)
    console.log("\n[Test 2] Parsing CSV without headers...");
    const csvNoHeader = "Alice,30,Developer\nBob,25,Designer";
    const csvNoHeaderResult = parseCsv(csvNoHeader, { hasHeader: false });
    console.log(csvNoHeaderResult);
    if (csvNoHeaderResult.length !== 2 || csvNoHeaderResult[0][0] !== "Alice" || csvNoHeaderResult[1][2] !== "Designer") {
        throw new Error("CSV parsing without headers failed!");
    }
    console.log("✓ CSV parsing without headers passed!");

    // 3. Fixed-Width Parse Test
    console.log("\n[Test 3] Parsing Fixed-Width text...");
    const fwText = "Alice     30        Developer \nBob       25        Designer  ";
    const specs = [
        { name: "Name", start: 0, length: 10 },
        { name: "Age", start: 10, length: 10 },
        { name: "Role", start: 20, length: 11 }
    ];
    const fwResult = parseFixedWidth(fwText, specs);
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

    // 5. LLM Structured Output Repair Test
    console.log("\n[Test 5] Repairing cutoff/LLM tabular output...");
    const rawLlm = "```csv\nName,Age\nAlice,30\nBob,25\n"; // missing closing quotes/markdown block
    const cleanCsv = repairTabularOutput(rawLlm);
    console.log("Repaired output:\n" + cleanCsv);
    if (!cleanCsv.startsWith("Name,Age") || cleanCsv.includes("```")) {
        throw new Error("LLM output repair failed!");
    }
    console.log("✓ LLM structured output repair passed!");

    console.log("\n--------------------------------------------------");
    console.log("✓ ALL WASM INTEGRATION TESTS PASSED!");
    console.log("--------------------------------------------------");
}

run().catch(err => {
    console.error("\n❌ WASM Integration Tests Failed:", err);
    process.exit(1);
});
