# Command Line Interface (CLI) Guide

[Back to README](../README.md)

The `heroparser` CLI is a high-performance, AI-native command-line utility for working with tabular data formats (CSV, JSONL, Fixed-Width, and Excel `.xlsx`). It is built using `HeroParser.Console` and supports Native AOT for high-speed file operations.

---

## 1. Installation

### Option 1: Dotnet Global Tool (Cross-Platform)

Install the CLI globally as a dotnet tool:

```bash
dotnet tool install --global HeroParser.Cli
```

To update an existing installation:

```bash
dotnet tool update --global HeroParser.Cli
```

### Option 2: Homebrew Tap (macOS & Linux)

For macOS and Linux users, you can install the native AOT-compiled binary using Homebrew via our custom tap:

```bash
brew tap KoalaFacts/heroparser
brew install heroparser
```

To update:

```bash
brew upgrade heroparser
```

### Option 3: Shell Installer Script (macOS & Linux fallback)

If you don't use Homebrew, you can install the standalone binary using our installer script:

```bash
curl -fsSL https://raw.githubusercontent.com/KoalaFacts/HeroParser/main/install.sh | sh
```

This will automatically detect your operating system and architecture, download the correct release asset, extract the binary, and install it to `/usr/local/bin` (or `~/.local/bin` if `/usr/local/bin` is not writable).

### Option 4: Snap Store (Linux, pending)

The Snap package is not yet publicly available. Use the shell installer or a Linux archive from GitHub Releases until store publication is confirmed.

### Option 5: WinGet (Windows)

On Windows, you can install the portable AOT-compiled executable via WinGet:

```bash
winget install KoalaFacts.HeroParser
```

### Option 6: Scoop (Windows)

Alternatively, you can install the binary using Scoop by adding our custom bucket:

```bash
scoop bucket add heroparser https://github.com/KoalaFacts/scoop-heroparser.git
scoop install heroparser/heroparser
```


---

## 2. Interactive Wizard

If you run `heroparser` without any arguments, it starts in **Interactive Wizard Mode**:

```bash
heroparser
```

This presents a styled, keyboard-driven navigation menu (powered by `HeroParser.Console`) where you can:
- Select a file to inspect.
- Choose from operations (quick inspect, detect delimiter, validate, profile, convert, etc.).
- Fill in parameters with auto-validation.

To load a file directly into the wizard:

```bash
heroparser products.csv
```

---

## 3. Subcommand Reference

### 3.1 `inspect`

Quickly inspect a UTF-8 CSV/TSV file without reading all data rows. The command reads up to 64 KiB to infer a delimiter, then inspects up to 1,000 data rows by default. It reports encoding evidence, delimiter confidence, column names and sample-only type/empty-value observations, row-width mismatches within the sample, and the first three data rows. It also prints a reusable `--delimiter` setting.

```bash
heroparser inspect data.csv
heroparser inspect data.tsv --delimiter '\t' --sample-rows 500
heroparser inspect data.csv --save-plan data.plan.json
```

`--sample-rows` accepts 1 to 10,000 and is rejected by other commands. Quick inspect supports at most 1,000 columns; wider inputs require a different workflow. Set `--delimiter` when automatic detection is uncertain or the file has only one column. Without a BOM, UTF-8/ASCII is assumed, not verified. This is not full-file validation; run `heroparser validate data.csv` for that. UTF-16, Excel and JSONL are not supported by quick inspect.

`--save-plan` creates a versioned JSON file containing the delimiter, header assumption, sampled column count, and sampling evidence. It refuses to overwrite an existing file or the input CSV. These values are observations, not a claim that the whole file is valid. Review the plan, especially when delimiter confidence is low, then run full-file validation with the same plan:

```bash
heroparser validate data.csv --plan data.plan.json
heroparser validate data.csv --plan data.plan.json --report data.validation.json
heroparser convert data.csv data.jsonl --plan data.plan.json
```

The plan currently assumes a header row and standard double-quote escaping. You may correct its delimiter before replaying it, but re-inspect if that changes the observed column count. `--plan` cannot be combined with `--delimiter`. Plans support UTF-8 `.csv` and `.tsv` input; planned conversion currently targets JSONL only. CSV-to-fixed-width and other conversion directions do not yet accept plans.

### 3.2 `detect`

Analyze a CSV file to auto-detect its delimiter character (e.g. `,`, `;`, `|`, `\t`) and character encoding (UTF-8, UTF-16, etc.) with a confidence score.

```bash
heroparser detect data.csv
```

### 3.3 `validate`

Verify the structural integrity of a file (e.g. checks that all rows have a consistent number of fields, detects unclosed quotes, and validates header structures).

```bash
heroparser validate data.csv
```

UTF-8 CSV files are validated row by row without loading the whole file. Validation retains at most 100 errors, then stops and reports that later rows were not checked. UTF-16 files with a BOM currently use the in-memory validation path. A validation failure returns a nonzero process exit code.

With `--plan`, validation uses the plan's delimiter and checks every row against its sampled column count. It still stops at the error limit, so a failure does not mean later rows were checked.

`--report <path>` writes a versioned JSON result with validated row count, column count, `stoppedEarly`, and the bounded set of errors with row and column numbers. It works with or without a plan and refuses to overwrite the input or an existing report. The validator does not currently expose byte offsets; the report does not invent them. Treat this report as containing potentially sensitive input-derived error details.

### 3.4 `profile`

Generate a markdown-formatted statistical profile card summarizing column datatypes, value ranges, distinct counts, null counts, and sample values. Ideal for printing dataset metadata or sending schema context to LLMs.

```bash
heroparser profile data.csv
```

UTF-8 CSV profiling reads rows incrementally. To keep memory bounded, it tracks at most 100 short values per categorical column; when that limit is reached, the displayed category count is a lower bound, not an exact distinct count. Excel and UTF-16 inputs currently use the in-memory path.

Options:
- `-d, --delimiter <char>`: Specify CSV delimiter.
- `-s, --sheet <name>`: Excel sheet name (if profiling an Excel workbook).

### 3.5 `convert`

Stream-convert records between CSV, JSONL, Fixed-Width, and Excel (`.xlsx`) formats.

```bash
heroparser convert input.csv output.jsonl
```

Options:
- `-sh, --shape <flat|openai|anthropic>`: Describe target JSON shape for CSV-to-JSONL conversions:
  - `flat`: Standard flat objects (default).
  - `openai`: OpenAI fine-tuning chat shape (`messages` array with system, user, and assistant content).
  - `anthropic`: Anthropic messages shape.
- `-d, --delimiter <char>`: Delimiter for input/output CSV.
- `--plan <path>`: Validate a UTF-8 CSV/TSV input against a saved plan before CSV/TSV-to-JSONL conversion. No output is created when validation fails.
- `-s, --sheet <name>`: Sheet name if input or output is an Excel file.

### 3.6 `repair`

Cleans up truncated, poorly-escaped, or cut-off tabular text returned by LLMs (e.g., handles unclosed quotes/escapes on final lines, and strips markdown code-blocks tags).

```bash
heroparser repair raw_llm_output.csv clean_output.csv
```

### 3.7 `schema`

Infers column datatypes and generates a production-ready C# record class model decorated with `[GenerateBinder]` and v2 mapping/validation attributes.

```bash
heroparser schema data.csv
```

AI-Powered Mode:
Add `--ai` to consult LLMs to infer optimal field-level validation rules (e.g., regex patterns for emails/zip codes, validation range limits, and enum type mapping):

```bash
heroparser schema data.csv --ai --ai-provider gemini
```

### 3.8 `query` / `ask` [AI]

Submit natural language questions about your dataset. The CLI profiles the data structure, extracts top rows, and uses LLMs to answer questions directly.

```bash
heroparser query data.csv "Which region generated the highest sales volume?"
```

### 3.9 `translate` [AI]

Translate, map, or transform cells across rows in batches utilizing an LLM prompt.

```bash
heroparser translate customers.csv "Translate the Description field to Spanish and capitalize the Name field" --output spanish_customers.csv
```

---

## 4. Option Reference

### 4.1 Global Options

| Option | Description |
|--------|-------------|
| `-d, --delimiter <char>` | Explicitly set CSV delimiter (e.g. `,`, `;`, `\|`, or `\t`). |
| `-s, --sheet <name>` | Sheet name to read from or write to for Excel (`.xlsx`) files. |
| `-o, --output <path>` | Target file path for outputs (required for `convert`, `repair`, `translate`). |

### 4.2 AI-Native Options

| Option | Description | Default |
|--------|-------------|---------|
| `-ai, --ai` | Enables LLM assistance for schemas and validation parsing. | `false` |
| `-p, --ai-provider <name>` | Select local AI CLI provider: `google`, `openai`, `anthropic`, `microsoft`, `github`, or `ollama`. | `google` |
| `-k, --ai-key <key>` | Not needed for local CLI providers (retained for compatibility). | None |
| `-m, --model <name>` | Override default model name for the local CLI (e.g. `gpt-5.5` or `qwen3.5:latest`). | Local default |
| `-b, --batch-size <num>` | Record batch size sent to the local CLI during translation. | `50` |

---

## 5. AI-Native Local-First Architecture

Unlike traditional CLI tools that make direct HTTP API calls to cloud services (requiring developers to configure API keys, worry about token costs, and expose credentials), HeroParser CLI uses a **Local-First AI-Native approach**.

### Key Differences & Benefits

1. **Zero-Key, Credentials-Free Integration**
   - By delegating queries directly to local AI developer CLI tools already authenticated on your system, you do not need to configure API keys or manage credentials inside HeroParser. It uses your active local sessions automatically.

2. **Supported Local AI CLIs**
   - **Google (`google` / `antigravity`)**: Runs on top of the Antigravity developer agent CLI (`agy -p - --dangerously-skip-permissions`).
   - **OpenAI (`openai`)**: Interlaces with the official `openai` CLI (`openai responses create --input -`) or falls back to Codex (`codex exec -`).
   - **Anthropic (`anthropic`)**: Leverages the Claude Code TUI (`claude -p - --permission-mode dontAsk --no-session-persistence`).
   - **Microsoft & GitHub (`microsoft` / `github`)**: Integrates with the unified Copilot agent CLI (`copilot -p - --allow-all -s`).
   - **Ollama (`ollama`)**: Runs fully local LLMs offline (defaulting to `qwen3.5:latest` or custom models) via `ollama run <model>`.

3. **Secure Process Lifecycle Management**
   - To prevent background resource leaks, HeroParser CLI strictly monitors spawned subprocesses. If a query is cancelled, interrupted, or times out (hard limit of 3 minutes), the CLI kills the entire spawned process tree (`process.Kill(entireProcessTree: true)`) immediately.

4. **Infinite Stream Safety (Stdin Piping)**
   - Instead of passing prompt content via command line arguments—which triggers the OS argument buffer limits (like the Windows 8,191-character command limit)—prompts are streamed into the CLI tools via standard input (stdin). This allows you to pass very large dataset profiles and samples safely.

5. **Clean Structured Parsing**
   - AI outputs can sometimes contain markdown code fences (e.g., ` ```csharp `) or chat preambles. HeroParser automatically cleans and strips conversational wrapping, extracting only the raw structured text or code for programmatic compilation.

---

[Back to README](../README.md)
