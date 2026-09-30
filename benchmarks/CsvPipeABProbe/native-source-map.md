# Segmented128 Native Hotspots to Frozen Source

## Scope and Provenance

This is an artifact-only source-location report, not a production change or a
performance result. It uses the first worker from
[capture 36787620269](https://github.com/KoalaFacts/HeroParser/actions/runs/36787620269)
and the accepted annotations from
[replay 36789199966](https://github.com/KoalaFacts/HeroParser/actions/runs/36789199966).
The original capture job failed before sampling worker two. Replay repaired
report processing without rebuilding, launching a workload or collecting samples.
All earlier failed controls remain retained in [results.md](results.md).

The parser, generator, models and consumer remain unchanged from frozen source
`89c06810e76c4623ebad3cfc89c4bcdef41acd59`. Source links below pin that commit;
they do not follow a later branch revision. Local analysis only read source,
native annotations, JIT metadata and the original PE/PDB files. No local .NET
build, test or benchmark ran, and no CI timing/diagnostic rerun was requested.

The frozen Git blobs for `Csv.PipeReader.cs`, `Csv.PipeSequenceReader.cs`,
`Csv.Read.Async.cs`, `CsvRow.cs` and `CsvPipeColumnText.cs` were independently
hashed as bytes. All five SHA-256 values match their original parser PDB document
checksums, tying the source expressions to the compilation's actual input rather
than relying only on a branch label.

| Evidence | Identity |
| --- | --- |
| Original artifact | `csv-pipe-native-36787620269-1` |
| Replay artifact | `csv-pipe-native-36789199966-1` |
| Worker / sampled thread | PID 3465 / TID 3465 |
| Runtime and placement | .NET 10.0.12, EPYC 7763, CPU 0, workstation GC |
| Event | `cpu-clock:u`, monotonic clock, 199 Hz |
| Samples / reported loss | 5967 / 0 |
| Unresolved sampled period | 6.2343% |
| Raw `a/cpu.perf.data` SHA-256 | `59d7127bef15bbf6667fb9906b1ec7c911de27f73b1dda3905ba34ca648ffd50` |

Native records are in original `a/jit-methods.json` and annotations in replay
`a/assembly-{1,2,3}-stdout.txt`. The code-load TID for the three methods is 3471;
it is not the sampled execution TID. JIT record indices identify code versions,
not managed metadata tokens.

## Address and Confidence Rules

Each annotated ELF function starts at `0x80`. Printed instruction offsets are
ELF virtual addresses, not offsets from the managed method's native entry:

```text
method-relative offset = ELF instruction address - 0x80
process instruction address = JIT code-load Address + method-relative offset
```

Process addresses below apply only to this capture; ASLR and tiering make them
unsuitable as durable source identifiers. Preserve the method version, record
index, native code hash and relative offset together.

| OptimizedTier1 method | JIT index / native base | Bytes | Exclusive process share | Code SHA-256 |
| --- | --- | ---: | ---: | --- |
| `CsvPipeSequenceReader.<MoveNextSlowAsync>d__31.MoveNext` | 14713 / `0x7f0274cd6540` | 9846 | 37.67% | `979b6c0de14ee933e21a5d457e211aa4b51b0484427dda7998b4a019a9018c74` |
| `Csv.TryBindPipeSequenceRow` | 14758 / `0x7f0274cdbbc0` | 12845 | 16.88% | `86d48d9b82ea75a0591cca88091b70e4435d543789a61857a33cf4ff6079dc61` |
| `Case.<CandidateAsync>d__8.MoveNext` | 14897 / `0x7f0274ce9980` | 10374 | 8.14% | `c4b489e2884fba80a40c79c848f1ba9f67831194a09fc80eec9705b4d6e65cf2` |

Method identity and machine instructions are directly observed. Source-expression
matches below are structural inferences from their operation order, constants,
branches and frozen source, not native-to-IL debug mappings. The available PDB
sequence points map IL to source; they cannot by themselves attribute an optimized
native address, an inlined call or a stack slot to a precise C# line/local.

Process shares use exclusive user-space sampled period, including benchmark
checksums, runtime and unresolved work. Instruction percentages instead use the
containing native method's sampled period. Inlined work is charged to its caller;
out-of-line work is not. Software clock sample IPs can skid. Neither denominator
is an exact instruction cost, inclusive source cost or expected speedup.

## Reader: Scalar Scan and Cursor Maintenance

The no-comment segmented path calls the column-aware `Csv.TryReadRow` from
[Csv.PipeSequenceReader.cs:584](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/src/HeroParser/Csv.PipeSequenceReader.cs#L584).
Its scalar loop starts at
[Csv.PipeReader.cs:179](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/src/HeroParser/Csv.PipeReader.cs#L179).
The annotated region has the same ordered operations inside the reader state
machine, consistent with an inlined implementation of that loop.

| ELF address | Method-relative | Observed operation | Frozen source match / confidence |
| --- | --- | --- | --- |
| `0x8a` | `0x0a` | `sub $0x3d8,%rsp` | Direct: fixed 984-byte reservation in this native method, excluding saved registers and dynamic allocations. No particular C# local is identified. |
| `0xb0..0xc6` | `0x30..0x46` | Three 16-byte zero stores per loop iteration | Direct: fixed-frame initialization, about 10.72% method-local sampled period. Source-local ownership remains unknown. |
| `0x5af..0x5ce` | `0x52f..0x54e` | Lazy-length check, length minus consumed, loop-exit test | Strong structural match: `while (reader.Remaining > 0)` at line 179. |
| `0x5d4..0x634` | `0x554..0x5b4` | End check, span/index bounds, byte load, index and consumed increments, segment-end branch | Strong structural match: `reader.TryRead(out byte current)` at line 181. |
| `0x63a..0x657` | `0x5ba..0x5d7` | Subtract one, overflow branch, signed 32-bit round-trip check | Strong structural match: `checked((int)(reader.Consumed - 1))` at line 184. |
| `0x65d..0x6de` | `0x5dd..0x65e` | State tests, delimiter comparison, CR/LF constants, back edge | Structural match: quote/escape state and delimiter/newline handling in the same loop; not exact line attribution for every branch. |

The .NET 10.0.12 implementation confirms the runtime operations used for this
comparison: [Remaining](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Memory/src/System/Buffers/SequenceReader.cs#L83)
subtracts `Consumed` from a lazily initialized length;
[TryRead](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Memory/src/System/Buffers/SequenceReader.cs#L176)
reads the current span, updates both counters and checks whether the segment ends.
This supports the structural match, not exact field-location debug information.

Two prominent sampled instruction addresses are now actionable source locations:

| ELF / relative / process address | Instruction | Method-local sampled period | Interpretation |
| --- | --- | ---: | --- |
| `0x62c` / `0x5ac` / `0x7f0274cd6aec` | Load span length before the segment-end comparison | 9.16% | `TryRead` cursor/segment bookkeeping, not a block-copy instruction. |
| `0x641` / `0x5c1` / `0x7f0274cd6b01` | `sub $0x1,%rcx` | 9.65% | Checked position calculation following `TryRead`; do not infer that subtraction alone consumes this fraction of CPU cycles. |

The `0x603` byte load carries 2.80% method-local period. The surrounding loop
demonstrates repeated cursor/position work per byte. It does not establish that
cursor bookkeeping dominates total copy cost or caused earlier A/A divergence.

## Binder: Fixed Frame Is Not the Row Scratch Buffer

[TryBindPipeSequenceRow](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/src/HeroParser/Csv.Read.Async.cs#L61)
has contiguous, short split-row and large split-row branches. They contain three
source-level `binder.TryBind` call sites (lines 70, 78 and 84).
[TryGetContiguousRow](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/src/HeroParser/Csv.PipeSequenceReader.cs#L833)
checks whether the record sequence itself is single-segment; a segmented input
does not imply that every individual record requires a copy.

| ELF address | Method-relative | Observed operation | Source match / confidence |
| --- | --- | --- | --- |
| `0x8a` | `0x0a` | `sub $0x638,%rsp` | Direct: fixed 1592-byte native reservation. This is not `rowLength`. |
| `0xb9..0xcf` | `0x39..0x4f` | Fixed-frame vector zeroing loop, before the contiguity branch | Direct: about 30.29% method-local sampled period. No exact C# local or inlined descendant can be assigned its bytes. |
| `0xec..0xf4` | `0x6c..0x74` | Compare sequence endpoint objects, branch to split-row path | Structural match: row contiguity check at source line 68. |
| `0xeac` | `0xe2c` | Compare row length with `0x400` | Strong structural match: 1024-byte stackalloc threshold at source line 74. |
| `0xeb9..0xedd` | `0xe39..0xe5d` | Round dynamic size to 16 bytes, loop over paired `push $0`, obtain buffer pointer | Strong structural match: `stackalloc byte[rowLength]` at source line 76, separate from fixed-frame zeroing. |
| `0xeeb..0xf15` | `0xe6b..0xe95` | Prepare sequence, destination pointer and row length, call a helper | Structural match: short split-row `RawRecord.CopyTo(scratch)` path at source line 77. The indirect helper's identity/body and inclusive cost are not established by this annotation. |

For example, fixed-frame loop start `0xb9` is process address
`0x7f0274cdbbf9`, whereas dynamic scratch loop start `0xed0` is
`0x7f0274cdca10`. Attributing the former's 30.29% to the latter would mix distinct
code regions. A low exclusive percentage at a helper call does not mean that the
helper or all cross-segment copies are cheap. Likewise, short-row scratch
initialization cannot explain initialization observed before branch selection.

### Original Generated Source, Not a Regenerated Approximation

The original artifact's `a/CsvPipeABModels.pdb` embeds
`CsvInlineByteSourceBinder.CsvPipeABModels_PipeRecord.g.cs`. Its original bytes
were recovered through the Portable PDB `EmbeddedSource` record, not by building
the generator again. The [Portable PDB specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#embedded-source-c-and-vb-compilers)
defines that record's serialization and the document checksum.

| Identity check | Value |
| --- | --- |
| Model DLL SHA-256 | `b784c6e9aa82fa434d0cc4fc903f9a38b41ea0f9e2d67eb6b05ac638dac717ce` |
| Model PE CodeView / PDB GUID | `08ed10b2-c48a-433e-a1a3-d6b123bce6af` |
| Matching PE / PDB stamp; CodeView age | `2418127640`; `1` |
| Embedded record kind | `0e8a571b-6926-466e-b4ad-8ab04611f5fe` |
| Declared / recovered uncompressed bytes | `9434` / `9434` |
| Document SHA-256, independently recomputed | `739711aae4c862cfabb690ac91653cd7a6895015f9f0dc8c87f5869b3da6bbfc` |

Reproduce without executing the assemblies: use `PEReader` to read CodeView,
`MetadataReaderProvider.FromPortablePdbStream` for the matching PDB, enumerate
its generated-byte-binder document and its `EmbeddedSource` custom record,
decode the positive int32 header using Deflate, then check recovered length and
SHA-256 against the document record. Hash the original bytes, including the UTF-8
BOM and original newlines; do not hash reformatted text. Negative format headers
are unsupported, not uncompressed data. This recovers compiler input only,
not the JIT's inlining tree or native local-variable map.

The authenticated generated source has these locations:

| Generated lines | Operation | Related frozen parser source |
| --- | --- | --- |
| 65-69 | Aggressive-inlined `TryBind`, construct `PipeRecord`, call `BindInto` | The wrapper's three `TryBind` call sites. |
| 72-87, 106-116 | Aggressive-inlined `BindInto`; `Id` and `Amount` use `GetValue` and `TryParseInt32` | Numeric field binding, not string decoding. |
| 95-103, 124-132 | `Name` and `Note` call `GetValueString`, check configured null values, assign strings | [CsvRow.cs:125](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/src/HeroParser/SeparatedValues/Reading/Rows/CsvRow.cs#L125). |

This confirms the source call chain, but does not prove that all three sites
produce full native clones or which generated locals inflate the fixed frame.
`GetValueString` routes byte fields to
[CsvPipeColumnText.Decode](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/src/HeroParser/SeparatedValues/Reading/Shared/CsvPipeColumnText.cs#L39).
Ordinary fields use UTF-8 decoding directly; escaping already goes through a
separate `DecodeEscaped` helper at line 61. That helper lacks forced inlining,
but this is not proof it is never inlined or is statistically cold. The trace
does not justify assigning the fixed frame to escaped decoding or adding an
inlining attribute as an optimization.

## Consumer: Retain Checksum Cost in the Denominator

The consumer's 8.14% process share is not wholly parser overhead. ELF addresses
`0x2126` and `0x2166` load FNV multiplier `0x100000001b3`, followed by character
XOR/multiply loops. These strongly match `Fixture.Hash`'s name/note loops at
[Program.cs:407](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/benchmarks/CsvPipeABProbe/Program.cs#L407),
called by the generated-path consumer at line 469. Their relative offsets are
`0x20a6` and `0x20e6`; process addresses are `0x7f0274ceba26` and
`0x7f0274ceba66`. The two multiplier loads carry 23.46% and 26.75% consumer-local
sampled period, respectively, not parser-local cost or exact multiply latency.
Do not remove the checksum or subtract this entire state machine from results:
that changes the established end-to-end workload and exceeds the attribution.

## Decision Boundary and Next Experiment

The strongest actionable source match is the reader's repeated `SequenceReader`
cursor/remaining/checked-position loop. The next production experiment candidate
is a bounded segment-local scan that reduces per-byte cursor updates while
preserving quote/escape state, CRLF and empty-segment boundaries, overflow checks,
resource limits and borrowed-row lifetime. Keep binding and decoding unchanged so
the experiment has one independent variable. This is a candidate, not an
implementation decision or a speedup claim.

The separate binder hypothesis is fixed-frame/local pressure around row
preparation and generated binding. A later diagnostic could test separating row
preparation from binding to reduce duplicated inlining opportunities; preserve
all three buffer lifetimes and custom binder semantics. Native-to-IL/inlining
evidence is still required before naming a particular local or clone as causal.
Do not combine that structural change with the reader experiment.

Current source-location status: method identities and instruction regions are
verified, reader/copy-path expressions are structurally matched, and generated
compiler input is authenticated. Precise optimized native-to-source-line/local
attribution remains unavailable. No second-worker comparison, old A/A root cause,
accepted throughput gain or merge approval follows. Failed timing acceptance
remains failed; any future timing trial needs a separately justified protocol,
unchanged correctness checks and fail-closed same-source controls before A/B.
