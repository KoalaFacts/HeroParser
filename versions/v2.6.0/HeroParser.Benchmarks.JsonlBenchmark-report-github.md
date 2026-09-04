```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 3.68GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------- |------- |----------:|-----------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ReadFromText**                   | **10000**  |  **3.110 ms** |  **0.0944 ms** | **0.0146 ms** |  **1.00** |    **0.01** |  **191.4063** |   **74.2188** |   **74.2188** |          **-** |          **NA** |
| ReadFromStream                 | 10000  |  2.980 ms |  0.0755 ms | 0.0196 ms |  0.96 |    0.01 |   89.8438 |         - |         - |   968520 B |          NA |
| ReadFromFileAsync              | 10000  |  5.426 ms |  0.5403 ms | 0.1403 ms |  1.74 |    0.04 |  187.5000 |         - |         - |  1878169 B |          NA |
| WriteToText                    | 10000  |  2.407 ms |  0.0384 ms | 0.0059 ms |  0.77 |    0.00 |  199.2188 |  199.2188 |  199.2188 |  2573108 B |          NA |
| WriteToStream                  | 10000  |  1.730 ms |  0.0876 ms | 0.0227 ms |  0.56 |    0.01 |   87.8906 |   87.8906 |   87.8906 |          - |          NA |
| ReadFromText_SourceGenerated   | 10000  |  2.408 ms |  0.0594 ms | 0.0154 ms |  0.77 |    0.01 |  117.1875 |   70.3125 |   70.3125 |          - |          NA |
| ReadFromStream_SourceGenerated | 10000  |  1.999 ms |  0.0111 ms | 0.0017 ms |  0.64 |    0.00 |   46.8750 |         - |         - |   488520 B |          NA |
| WriteToText_SourceGenerated    | 10000  |  2.071 ms |  0.1119 ms | 0.0290 ms |  0.67 |    0.01 |  187.5000 |  187.5000 |  187.5000 |  2572786 B |          NA |
| WriteToStream_SourceGenerated  | 10000  |  1.488 ms |  0.0252 ms | 0.0065 ms |  0.48 |    0.00 |   64.4531 |   64.4531 |   64.4531 |   646714 B |          NA |
| ConvertCsvToJsonlFlat          | 10000  |  3.425 ms |  0.1208 ms | 0.0314 ms |  1.10 |    0.01 |  328.1250 |  300.7813 |  300.7813 |  3509145 B |          NA |
| ConvertJsonlToCsv              | 10000  |  5.864 ms |  0.4478 ms | 0.0693 ms |  1.89 |    0.02 |  382.8125 |  234.3750 |  132.8125 |  4901279 B |          NA |
|                                |        |           |            |           |       |         |           |           |           |            |             |
| **ReadFromText**                   | **100000** | **29.824 ms** |  **0.8750 ms** | **0.2272 ms** |  **1.00** |    **0.01** | **1750.0000** |  **656.2500** |  **656.2500** |          **-** |          **NA** |
| ReadFromStream                 | 100000 | 27.161 ms |  0.2289 ms | 0.0354 ms |  0.91 |    0.01 | 1031.2500 |   31.2500 |   31.2500 | 37857708 B |          NA |
| ReadFromFileAsync              | 100000 | 57.199 ms | 15.2687 ms | 3.9652 ms |  1.92 |    0.12 | 1666.6667 |         - |         - | 19878296 B |          NA |
| WriteToText                    | 100000 | 18.256 ms |  0.7220 ms | 0.1875 ms |  0.61 |    0.01 |  343.7500 |  343.7500 |  343.7500 | 26309805 B |          NA |
| WriteToStream                  | 100000 | 16.526 ms |  0.2466 ms | 0.0640 ms |  0.55 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6755856 B |          NA |
| ReadFromText_SourceGenerated   | 100000 | 22.427 ms |  0.8101 ms | 0.2104 ms |  0.75 |    0.01 |  937.5000 |  468.7500 |  468.7500 | 11563444 B |          NA |
| ReadFromStream_SourceGenerated | 100000 | 20.344 ms |  0.2062 ms | 0.0535 ms |  0.68 |    0.01 |  468.7500 |         - |         - |  4808520 B |          NA |
| WriteToText_SourceGenerated    | 100000 | 15.621 ms |  0.7176 ms | 0.1864 ms |  0.52 |    0.01 |  375.0000 |  375.0000 |  375.0000 | 26310160 B |          NA |
| WriteToStream_SourceGenerated  | 100000 | 15.100 ms |  0.2292 ms | 0.0595 ms |  0.51 |    0.00 |  156.2500 |  156.2500 |  156.2500 |          - |          NA |
| ConvertCsvToJsonlFlat          | 100000 |        NA |         NA |        NA |     ? |       ? |        NA |        NA |        NA |         NA |           ? |
| ConvertJsonlToCsv              | 100000 | 61.713 ms |  5.0146 ms | 1.3023 ms |  2.07 |    0.04 | 2000.0000 | 1500.0000 | 1000.0000 | 61569188 B |          NA |

Benchmarks with issues:
  JsonlBenchmark.ConvertCsvToJsonlFlat: Job-INMAZI(IterationCount=5, RunStrategy=Throughput, WarmupCount=3) [Rows=100000]
