# Command Line Interface (CLI) Guide

[Back to README](../README.md)

The `heroparser` CLI is a high-performance, AI-native command-line utility for working with tabular data formats (CSV, JSONL, Fixed-Width, and Excel `.xlsx`). It is built using `HeroParser.Console`, ensuring complete Native AOT compatibility and zero-allocation overhead for high-speed file operations.

---

## 1. Installation

Install the CLI globally as a dotnet tool:

```bash
dotnet tool install --global HeroParser.Cli --version 2.5.0
```

To update an existing installation:

```bash
dotnet tool update --global HeroParser.Cli --version 2.5.0
```

Once installed, verify the installation by running:

```bash
heroparser --help
```

---

## 2. Interactive Wizard

If you run `heroparser` without any arguments, it starts in **Interactive Wizard Mode**:

```bash
heroparser
```

This presents a styled, keyboard-driven navigation menu (powered by `HeroParser.Console`) where you can:
- Select a file to inspect.
- Choose from operations (detect delimiter, validate, profile, convert, repair, etc.).
- Fill in parameters with auto-validation.

To load a file directly into the wizard:

```bash
heroparser products.csv
```

---

## 3. Subcommand Reference

### 3.1 `detect`

Analyze a CSV file to auto-detect its delimiter character (e.g. `,`, `;`, `|`, `\t`) and character encoding (UTF-8, UTF-16, etc.) with a confidence score.

```bash
heroparser detect data.csv
```

### 3.2 `validate`

Verify the structural integrity of a file (e.g. checks that all rows have a consistent number of fields, detects unclosed quotes, and validates header structures).

```bash
heroparser validate data.csv
```

### 3.3 `profile`

Generate a markdown-formatted statistical profile card summarizing column datatypes, value ranges, distinct counts, null counts, and sample values. Ideal for printing dataset metadata or sending schema context to LLMs.

```bash
heroparser profile data.csv
```

Options:
- `-d, --delimiter <char>`: Specify CSV delimiter.
- `-s, --sheet <name>`: Excel sheet name (if profiling an Excel workbook).

### 3.4 `convert`

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
- `-s, --sheet <name>`: Sheet name if input or output is an Excel file.

### 3.5 `repair`

Cleans up truncated, poorly-escaped, or cut-off tabular text returned by LLMs (e.g., handles unclosed quotes/escapes on final lines, and strips markdown code-blocks tags).

```bash
heroparser repair raw_llm_output.csv clean_output.csv
```

### 3.6 `schema`

Infers column datatypes and generates a production-ready C# record class model decorated with `[GenerateBinder]` and v2 mapping/validation attributes.

```bash
heroparser schema data.csv
```

AI-Powered Mode:
Add `--ai` to consult LLMs to infer optimal field-level validation rules (e.g., regex patterns for emails/zip codes, validation range limits, and enum type mapping):

```bash
heroparser schema data.csv --ai --ai-provider gemini
```

### 3.7 `query` / `ask` [AI]

Submit natural language questions about your dataset. The CLI profiles the data structure, extracts top rows, and uses LLMs to answer questions directly.

```bash
heroparser query data.csv "Which region generated the highest sales volume?"
```

### 3.8 `translate` [AI]

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
| `-p, --ai-provider <name>` | Select provider: `gemini`, `openai`, or `anthropic`. | `gemini` |
| `-k, --ai-key <key>` | API key (falls back to environmental variables like `GEMINI_API_KEY`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`). | Env value |
| `-m, --model <name>` | Override default model name (e.g. `gemini-1.5-pro` or `gpt-4o`). | Provider default |
| `-b, --batch-size <num>` | Record batch size sent to the LLM during translation. | `50` |

---

[Back to README](../README.md)
