---
name: verify-heroparser
description: How to exercise HeroParser for real (CLI, AOT native binary, Wasm npm package, browser demo) and which evidence proves a change works; use with verification-first.
---

# Verify HeroParser

Use this together with `verification-first`. Unit tests are the floor, not the proof: the real boundary is the `heroparser` CLI and its Native AOT binary run on real CSV / fixed-width / Excel / JSONL files on net10.0 and net11.0, plus the Wasm npm package and the browser demo when `npm/` or `src/HeroParser.Wasm/` change.

## Setup

```bash
# SDKs into one scratch folder, first on PATH (global.json pins an 11.0 SDK floor with rollForward latestFeature)
curl -sSL https://dot.net/v1/dotnet-install.sh -o di.sh
bash di.sh --channel 11.0 --quality preview --install-dir "$SCRATCH/dotnet"
bash di.sh --channel 10.0 --install-dir "$SCRATCH/dotnet"
export PATH="$SCRATCH/dotnet:$PATH" DOTNET_ROOT="$SCRATCH/dotnet"
# HeroParser.Wasm targets net10.0 in the browser: the solution does not build without this workload
dotnet workload install wasm-tools-net10
dotnet restore HeroParser.slnx --force-evaluate
dotnet build HeroParser.slnx -c Release
```

Node 24+ and npm 12 for `npm/` (`npm install --prefix "$SCRATCH/npm12" npm@12`, then put `$SCRATCH/npm12/node_modules/.bin` first on PATH; the demo's `prebuild` runs `require-npm12.js` and refuses older npm).

Run the CLI from the build output, per framework:

```bash
CLI="dotnet src/HeroParser.Cli/bin/Release/net11.0/HeroParser.Cli.dll"   # or net10.0
$CLI inspect data.csv
```

Native AOT binary (the shipped CLI shape):

```bash
dotnet publish src/HeroParser.Cli -c Release -f net11.0 -r linux-x64 -o "$SCRATCH/cli-aot"
"$SCRATCH/cli-aot/HeroParser.Cli" inspect data.csv   # the native binary keeps the project name; packages rename it to heroparser
```

Small real inputs that hit the interesting paths:

```bash
printf 'Id,Name,Price,Region\n1,Widget,9.99,North\n2,"Gadget, large",19.50,South\n3,Thing,0.5,North\n' > data.csv
printf 'Id,Name\n1,a\n2,b,extra\n3,"open\n' > bad.csv            # width mismatch + unclosed quote
printf '```csv\nId,Name\n1,"Bob\n' > llm.txt                     # LLM-style truncated output for repair
```

## Feature map

Every row was run once against `main` at the commit in "Last verified" (net10.0 and net11.0 framework-dependent builds; AOT and npm rows as stated).

| Feature | Code entry points | How to exercise it for real | Evidence to capture | Neighbours at risk |
|---|---|---|---|---|
| Quick inspect | `src/HeroParser.Cli/CliCommands.Inspect.cs`, `CsvDelimiterDetector` | `$CLI inspect data.csv`; `--save-plan p.json` then `validate --plan p.json` | Delimiter `,` at 100/100, `Columns 4`, `Data rows inspected 3`, exit 0; saved plan refuses to overwrite | detect, validate `--plan`, convert `--plan` |
| Delimiter/encoding detect | `CliCommands.cs` (`detect`), `src/HeroParser/SeparatedValues/Detection/` | `$CLI detect data.csv` | Detected delimiter and confidence table, exit 0 | inspect, validate auto-detect |
| Structural validation | `CliCommands.cs` (`validate`), `CsvValidationReport.cs`, `src/HeroParser/SeparatedValues/Validation/` | `$CLI validate data.csv`; `$CLI validate bad.csv`; `--report r.json` | Valid file: "File structure is completely valid", exit 0. `bad.csv`: error table (unclosed quote "quote started at position 2"), **exit 1** | inspect plans, row parser diagnostics (`CsvRowParser.ParseRow`) |
| Profile | `DynamicProfiler.cs`, `Utf8ProfileCache.cs`, `CsvRowBatchSource.cs` | `$CLI profile data.csv` | Types Integer/String/Decimal/String, ranges `[1.00 to 3.00]`, category counts, exit 0 | query/ask (uses the same profile), schema |
| Schema → C# record | `CliCommands.cs` (`SchemaAsync`), `Csv.InferSchema` | `$CLI schema data.csv` | `[GenerateBinder] public sealed class Data` with `int Id`, `double Price`, exit 0; compile the class against `src/HeroParser` when the change touches generation | source generators, `[TabularMap]`/`[Validate]` attributes |
| Convert CSV ↔ JSONL | `CliCommands.cs` (`Convert`), `JsonlToCsvConverter` | `$CLI convert data.csv o.jsonl` then `$CLI convert o.jsonl o2.csv` | JSONL objects with string values; `"Gadget, large"` unquoted in JSON and re-quoted in CSV; round trip identical to `data.csv` except that the CSV writer emits CRLF line endings | `--shape openai/anthropic`, `--plan` |
| Convert CSV → fixed-width | `CliCommands.cs`, `CsvToFixedWidthConverter` | `$CLI convert data.csv o.txt` | Padded columns sized max length + 2 (min 5), header included | fixed-width readers, `FixedWidthToCsvConverter` |
| Convert Excel → CSV/JSONL | `CliCommands.cs` (`inExt == ".xlsx"`), `src/HeroParser/Excels/` | needs a real `.xlsx`: the repo has no checked-in workbook, so write one with `Excel.WriteToFile(path, records)` from a scratch net11.0 console project that references `src/HeroParser/HeroParser.csproj`; then `$CLI convert in.xlsx o.csv` | Rows match the records written (output starts with a UTF-8 BOM); `-s Nope` reports "Sheet 'Nope' not found in workbook.", exit 1 | `ExcelRowBatchSource`, profile/query on `.xlsx` |
| Repair LLM output | `CliCommands.cs` (`Repair`) | `$CLI repair llm.txt fixed.csv` | Fence stripped, unclosed quote closed: `Id,Name` / `1,"Bob"`, exit 0 | — |
| AI commands (`query`, `translate`, `schema --ai`) | `src/HeroParser.Cli/AI/` | Only with a local AI CLI installed; never call a paid API to verify. Without one, exercise the prompt/parsing code through `HeroParser.Tests` | Prompt text and parsed output in tests; say "not run for real" in the PR | profile (prompt input) |
| Library across formats under AOT | `tests/HeroParser.AotTests/` (CSV, Fixed-Width, Excel, JSONL, HTB, Console) | `dotnet run --project tests/HeroParser.AotTests -c Release -f net11.0`, then `dotnet publish tests/HeroParser.AotTests -c Release -f net11.0 -r linux-x64 -o "$SCRATCH/aot-tests"` and run the native binary | Summary line with all passed, exit 0; `grep -E "IL[0-9]{4}\|warning" publish.log` empty | generators (reflection-free binders), trimming annotations |
| CLI as Native AOT | `src/HeroParser.Cli` (`PublishAot`) | publish as in Setup, rerun the inspect / validate / convert rows with the native binary | Same output as the framework-dependent run; no IL/trim warnings in the publish log | everything the CLI calls |
| Wasm npm package | `src/HeroParser.Wasm/`, `npm/packages/heroparser/` | `dotnet publish src/HeroParser.Wasm/HeroParser.Wasm.csproj -c Release`; `npm ci --prefix npm --legacy-peer-deps`; `(cd npm/tests && node test-wasm.js)` | test-wasm output with every check passing, exit 0 | browser demo |
| Browser demo | `npm/examples/browser-demo/` (Vue, `base: "./"`, deployed under `/HeroParser/demo/` by `deploy-browser-demo.yml`) | `npm run build --prefix npm --workspace=browser-demo --legacy-peer-deps`; serve `dist/` under `/HeroParser/demo/` and load it in headless Chromium (Playwright is preinstalled in cloud sessions) | Page title "HeroParser WASM", clicking **Parse CSV** on the built-in sample shows a "JSON Output" array and "Records: 2", no console or page errors; screenshot path | Wasm package |

## Gate

```bash
dotnet restore HeroParser.slnx --force-evaluate
dotnet build HeroParser.slnx -c Release                       # warnings are errors
dotnet format whitespace --verify-no-changes HeroParser.slnx
for f in net10.0 net11.0; do
  for p in HeroParser.Tests HeroParser.Generators.Tests HeroParser.Console.Tests; do
    dotnet test --project tests/$p -c Release -f $f --no-build
  done
done
dotnet run --project tests/HeroParser.AotTests -c Release -f net11.0
dotnet publish tests/HeroParser.AotTests -c Release -f net11.0 -r linux-x64   # then run the native binary
```

When `npm/` or `src/HeroParser.Wasm/` change, add the Wasm npm package and browser demo rows above, plus `vue-tsc -b` and `vp fmt --check` in the demo workspace.

## Traps

- `$CLI ... | head; echo $?` reports `head`'s exit code, not the CLI's. Redirect to a file and check `$?` directly; `validate` must exit 1 on a bad file.
- The CLI's own messages ("✗ Error: ... already exists", "Unsupported CSV import plan version") appear in `HeroParser.Tests` output because tests drive the CLI's rejection paths; they are expected, not failures. Read the "Test run summary" lines.
- A test without a `Category` trait is never run by CI (`TestCategoryCoverageTests` catches it); a local unfiltered `dotnet test` still runs it, so a local pass can hide a CI gap.
- `dotnet test --filter` is VSTest syntax and is ignored or rejected under Microsoft.Testing.Platform; use `-- --filter-trait`.
- A green build without `wasm-tools-net10` is impossible; if the solution builds but `HeroParser.Wasm` was skipped (building single projects), the npm rows were not verified.
- CSV → JSONL output keeps every value as a JSON string (`"Id":"1"`); that is current behaviour, not a typing bug introduced by a change.
- Not every documented conversion direction exists: fixed-width → CSV and CSV → `.xlsx` return "requires column widths" / "Unsupported conversion direction" with exit 1 (see "Known gaps").
- The CLI must not set `UseSystemResourceKeys` (it would print .NET errors as resource keys such as `IO_FileExists_Name`); `CliRuntimeConfigTests` checks the built `HeroParser.Cli.runtimeconfig.json`, but the AOT binary is only checked by running it: `inspect data.csv --save-plan <existing file>` must print `The file '<path>' already exists.`
- Performance claims need CLAUDE.md's same-runner A/B; a local benchmark run proves nothing under ~20%.

## Known gaps found while writing this skill

Observed on `main` at the "Last verified" commit; each is a separate ledger item, not fixed here.

- `profile` and CSV → fixed-width use the raw quoted field text: `"Gadget, large"` shows as `""Gadget, large""` in the profile categories and as `"Gadget, large"` (with quotes) in the fixed-width output, while CSV → JSONL unquotes it correctly.
- `docs/cli.md` says `convert` works "between CSV, JSONL, Fixed-Width, and Excel"; fixed-width → CSV and CSV → Excel are rejected by the CLI.

## Last verified

2026-11-06, `main` at `2309336`, SDK 11.0.100-rc.3.26522.148 and 10.0.403, linux-x64.
