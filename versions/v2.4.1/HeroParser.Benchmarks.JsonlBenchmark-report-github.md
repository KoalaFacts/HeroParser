```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.87GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------- |------- |----------:|----------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ReadFromText**                   | **10000**  |  **3.893 ms** | **0.0426 ms** | **0.0066 ms** |  **1.00** |    **0.00** |  **355.4688** |  **128.9063** |  **125.0000** |          **-** |          **NA** |
| ReadFromStream                 | 10000  |  3.462 ms | 0.0377 ms | 0.0058 ms |  0.89 |    0.00 |  125.0000 |         - |         - |   968520 B |          NA |
| ReadFromFileAsync              | 10000  |  7.412 ms | 1.0025 ms | 0.2603 ms |  1.90 |    0.06 |  195.3125 |    7.8125 |    7.8125 |          - |          NA |
| WriteToText                    | 10000  |  3.054 ms | 0.1249 ms | 0.0324 ms |  0.78 |    0.01 |  195.3125 |  195.3125 |  195.3125 |  2572775 B |          NA |
| WriteToStream                  | 10000  |  2.359 ms | 0.0934 ms | 0.0242 ms |  0.61 |    0.01 |   89.8438 |   89.8438 |   89.8438 |          - |          NA |
| ReadFromText_SourceGenerated   | 10000  |  2.941 ms | 0.0391 ms | 0.0101 ms |  0.76 |    0.00 |  121.0938 |   70.3125 |   70.3125 |          - |          NA |
| ReadFromStream_SourceGenerated | 10000  |  2.586 ms | 0.0277 ms | 0.0072 ms |  0.66 |    0.00 |   46.8750 |         - |         - |   488520 B |          NA |
| WriteToText_SourceGenerated    | 10000  |  2.579 ms | 0.1216 ms | 0.0188 ms |  0.66 |    0.00 |  199.2188 |  199.2188 |  199.2188 |  2572823 B |          NA |
| WriteToStream_SourceGenerated  | 10000  |  1.877 ms | 0.0540 ms | 0.0140 ms |  0.48 |    0.00 |   64.4531 |   64.4531 |   64.4531 |   646686 B |          NA |
| ConvertCsvToJsonlFlat          | 10000  |  4.446 ms | 0.4761 ms | 0.1236 ms |  1.14 |    0.03 |  312.5000 |  296.8750 |  296.8750 |  3509024 B |          NA |
| ConvertJsonlToCsv              | 10000  |  7.725 ms | 0.3726 ms | 0.0968 ms |  1.98 |    0.02 |  390.6250 |  234.3750 |  140.6250 | 19577304 B |          NA |
|                                |        |           |           |           |       |         |           |           |           |            |             |
| **ReadFromText**                   | **100000** | **39.197 ms** | **1.6645 ms** | **0.2576 ms** |  **1.00** |    **0.01** | **1923.0769** |  **923.0769** |  **923.0769** | **17083537 B** |        **1.00** |
| ReadFromStream                 | 100000 | 35.608 ms | 0.4694 ms | 0.1219 ms |  0.91 |    0.01 | 1000.0000 |         - |         - | 10328520 B |        0.60 |
| ReadFromFileAsync              | 100000 | 72.991 ms | 8.3689 ms | 2.1734 ms |  1.86 |    0.05 | 1666.6667 |         - |         - | 19878296 B |        1.16 |
| WriteToText                    | 100000 | 24.538 ms | 0.4098 ms | 0.1064 ms |  0.63 |    0.00 |  343.7500 |  343.7500 |  343.7500 | 26310678 B |        1.54 |
| WriteToStream                  | 100000 | 20.797 ms | 0.6006 ms | 0.0929 ms |  0.53 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6756064 B |        0.40 |
| ReadFromText_SourceGenerated   | 100000 | 28.600 ms | 1.6959 ms | 0.2624 ms |  0.73 |    0.01 |  937.5000 |  468.7500 |  468.7500 | 11562914 B |        0.68 |
| ReadFromStream_SourceGenerated | 100000 | 25.960 ms | 0.2540 ms | 0.0660 ms |  0.66 |    0.00 |  468.7500 |         - |         - |  4808520 B |        0.28 |
| WriteToText_SourceGenerated    | 100000 | 20.348 ms | 1.2678 ms | 0.1962 ms |  0.52 |    0.01 |  375.0000 |  375.0000 |  375.0000 | 26310241 B |        1.54 |
| WriteToStream_SourceGenerated  | 100000 | 17.928 ms | 0.2240 ms | 0.0582 ms |  0.46 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6756086 B |        0.40 |
| ConvertCsvToJsonlFlat          | 100000 |        NA |        NA |        NA |     ? |       ? |        NA |        NA |        NA |         NA |           ? |
| ConvertJsonlToCsv              | 100000 | 82.584 ms | 4.7282 ms | 1.2279 ms |  2.11 |    0.03 | 2000.0000 | 1000.0000 | 1000.0000 | 61574072 B |        3.60 |

Benchmarks with issues:
  JsonlBenchmark.ConvertCsvToJsonlFlat: Job-INMAZI(IterationCount=5, RunStrategy=Throughput, WarmupCount=3) [Rows=100000]
