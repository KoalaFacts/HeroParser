```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.74GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------- |------- |----------:|-----------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ReadFromText**                   | **10000**  |  **3.802 ms** |  **0.0965 ms** | **0.0251 ms** |  **1.00** |    **0.01** |  **207.0313** |   **85.9375** |   **85.9375** |  **5354553 B** |        **1.00** |
| ReadFromStream                 | 10000  |  3.442 ms |  0.0436 ms | 0.0067 ms |  0.91 |    0.01 |   89.8438 |         - |         - |   968520 B |        0.18 |
| ReadFromFileAsync              | 10000  |  7.228 ms |  0.5756 ms | 0.1495 ms |  1.90 |    0.04 |  195.3125 |    7.8125 |    7.8125 |          - |        0.00 |
| WriteToText                    | 10000  |  3.282 ms |  0.1178 ms | 0.0182 ms |  0.86 |    0.01 |  195.3125 |  195.3125 |  195.3125 |  2572746 B |        0.48 |
| WriteToStream                  | 10000  |  2.344 ms |  0.0924 ms | 0.0240 ms |  0.62 |    0.01 |   66.4063 |   66.4063 |   66.4063 |          - |        0.00 |
| ReadFromText_SourceGenerated   | 10000  |  2.942 ms |  0.0980 ms | 0.0254 ms |  0.77 |    0.01 |  171.8750 |   93.7500 |   93.7500 |          - |        0.00 |
| ReadFromStream_SourceGenerated | 10000  |  2.643 ms |  0.0117 ms | 0.0018 ms |  0.70 |    0.00 |   46.8750 |         - |         - |   488520 B |        0.09 |
| WriteToText_SourceGenerated    | 10000  |  2.626 ms |  0.1562 ms | 0.0406 ms |  0.69 |    0.01 |  183.5938 |  183.5938 |  183.5938 |  2572691 B |        0.48 |
| WriteToStream_SourceGenerated  | 10000  |  1.864 ms |  0.0529 ms | 0.0137 ms |  0.49 |    0.00 |   62.5000 |   62.5000 |   62.5000 |   646642 B |        0.12 |
| ConvertCsvToJsonlFlat          | 10000  |  4.591 ms |  0.3118 ms | 0.0810 ms |  1.21 |    0.02 |  320.3125 |  304.6875 |  304.6875 |  3509086 B |        0.66 |
| ConvertJsonlToCsv              | 10000  |  8.102 ms |  0.6425 ms | 0.1669 ms |  2.13 |    0.04 |  468.7500 |  328.1250 |  203.1250 |  5910456 B |        1.10 |
|                                |        |           |            |           |       |         |           |           |           |            |             |
| **ReadFromText**                   | **100000** | **38.432 ms** |  **1.6896 ms** | **0.4388 ms** |  **1.00** |    **0.01** | **1923.0769** |  **923.0769** |  **923.0769** | **17084286 B** |        **1.00** |
| ReadFromStream                 | 100000 | 37.731 ms |  0.5243 ms | 0.1362 ms |  0.98 |    0.01 | 1000.0000 |         - |         - | 10328520 B |        0.60 |
| ReadFromFileAsync              | 100000 | 75.515 ms | 10.2493 ms | 2.6617 ms |  1.97 |    0.07 | 2000.0000 |         - |         - | 19878296 B |        1.16 |
| WriteToText                    | 100000 | 23.272 ms |  1.6292 ms | 0.4231 ms |  0.61 |    0.01 |  375.0000 |  375.0000 |  375.0000 | 26310166 B |        1.54 |
| WriteToStream                  | 100000 | 22.392 ms |  0.3474 ms | 0.0902 ms |  0.58 |    0.01 |  187.5000 |  187.5000 |  187.5000 |  6755908 B |        0.40 |
| ReadFromText_SourceGenerated   | 100000 | 28.681 ms |  0.7100 ms | 0.1844 ms |  0.75 |    0.01 |  937.5000 |  468.7500 |  468.7500 | 11563069 B |        0.68 |
| ReadFromStream_SourceGenerated | 100000 | 26.127 ms |  0.1754 ms | 0.0271 ms |  0.68 |    0.01 |  468.7500 |         - |         - |  4808520 B |        0.28 |
| WriteToText_SourceGenerated    | 100000 | 19.818 ms |  0.5617 ms | 0.1459 ms |  0.52 |    0.01 |  343.7500 |  343.7500 |  343.7500 | 26310406 B |        1.54 |
| WriteToStream_SourceGenerated  | 100000 | 17.914 ms |  0.3934 ms | 0.0609 ms |  0.47 |    0.01 |  187.5000 |  187.5000 |  187.5000 |  6755912 B |        0.40 |
| ConvertCsvToJsonlFlat          | 100000 |        NA |         NA |        NA |     ? |       ? |        NA |        NA |        NA |         NA |           ? |
| ConvertJsonlToCsv              | 100000 | 85.168 ms |  9.2480 ms | 2.4017 ms |  2.22 |    0.06 | 2000.0000 | 1000.0000 | 1000.0000 | 61574072 B |        3.60 |

Benchmarks with issues:
  JsonlBenchmark.ConvertCsvToJsonlFlat: Job-INMAZI(IterationCount=5, RunStrategy=Throughput, WarmupCount=3) [Rows=100000]
