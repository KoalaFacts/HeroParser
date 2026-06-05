```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------- |------- |----------:|-----------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ReadFromText**                   | **10000**  |  **3.775 ms** |  **0.2000 ms** | **0.0309 ms** |  **1.00** |    **0.01** |  **460.9375** |  **160.1563** |  **152.3438** |          **-** |          **NA** |
| ReadFromStream                 | 10000  |  3.432 ms |  0.0567 ms | 0.0147 ms |  0.91 |    0.01 |   93.7500 |         - |         - |   968520 B |          NA |
| ReadFromFileAsync              | 10000  |  7.320 ms |  0.8401 ms | 0.2182 ms |  1.94 |    0.05 |  187.5000 |         - |         - |  1878173 B |          NA |
| WriteToText                    | 10000  |  3.496 ms |  0.4167 ms | 0.1082 ms |  0.93 |    0.03 |  292.9688 |  273.4375 |  273.4375 |  3389128 B |          NA |
| WriteToStream                  | 10000  |  2.376 ms |  0.0509 ms | 0.0079 ms |  0.63 |    0.00 |   66.4063 |   66.4063 |   66.4063 |          - |          NA |
| ReadFromText_SourceGenerated   | 10000  |  2.907 ms |  0.1441 ms | 0.0374 ms |  0.77 |    0.01 |  113.2813 |   66.4063 |   66.4063 |  1134131 B |          NA |
| ReadFromStream_SourceGenerated | 10000  |  2.540 ms |  0.0085 ms | 0.0013 ms |  0.67 |    0.00 |   46.8750 |         - |         - |   488520 B |          NA |
| WriteToText_SourceGenerated    | 10000  |  3.206 ms |  0.2334 ms | 0.0606 ms |  0.85 |    0.02 |  292.9688 |  273.4375 |  273.4375 |  3389099 B |          NA |
| WriteToStream_SourceGenerated  | 10000  |  1.836 ms |  0.0574 ms | 0.0149 ms |  0.49 |    0.01 |   64.4531 |   64.4531 |   64.4531 |   646673 B |          NA |
| ConvertCsvToJsonlFlat          | 10000  |  4.749 ms |  0.5616 ms | 0.0869 ms |  1.26 |    0.02 |  523.4375 |  312.5000 |  312.5000 |  5546468 B |          NA |
| ConvertJsonlToCsv              | 10000  |  7.532 ms |  0.8305 ms | 0.2157 ms |  2.00 |    0.05 |  531.2500 |  351.5625 |  210.9375 |          - |          NA |
|                                |        |           |            |           |       |         |           |           |           |            |             |
| **ReadFromText**                   | **100000** | **38.351 ms** |  **1.3520 ms** | **0.2092 ms** |  **1.00** |    **0.01** | **1923.0769** |  **923.0769** |  **923.0769** | **17084193 B** |        **1.00** |
| ReadFromStream                 | 100000 | 34.884 ms |  0.2884 ms | 0.0446 ms |  0.91 |    0.00 | 1000.0000 |         - |         - | 10328520 B |        0.60 |
| ReadFromFileAsync              | 100000 | 78.717 ms | 29.6993 ms | 7.7128 ms |  2.05 |    0.18 | 2000.0000 |         - |         - | 19878368 B |        1.16 |
| WriteToText                    | 100000 | 26.940 ms |  0.6226 ms | 0.1617 ms |  0.70 |    0.01 |  531.2500 |  531.2500 |  531.2500 | 30286860 B |        1.77 |
| WriteToStream                  | 100000 | 22.003 ms |  0.3738 ms | 0.0971 ms |  0.57 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6755591 B |        0.40 |
| ReadFromText_SourceGenerated   | 100000 | 27.916 ms |  0.7488 ms | 0.1945 ms |  0.73 |    0.01 |  937.5000 |  468.7500 |  468.7500 | 11563395 B |        0.68 |
| ReadFromStream_SourceGenerated | 100000 | 25.757 ms |  0.4667 ms | 0.1212 ms |  0.67 |    0.00 |  468.7500 |         - |         - |  4808520 B |        0.28 |
| WriteToText_SourceGenerated    | 100000 | 22.991 ms |  1.0388 ms | 0.2698 ms |  0.60 |    0.01 |  531.2500 |  531.2500 |  531.2500 | 30287330 B |        1.77 |
| WriteToStream_SourceGenerated  | 100000 | 17.647 ms |  0.3094 ms | 0.0479 ms |  0.46 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6755754 B |        0.40 |
| ConvertCsvToJsonlFlat          | 100000 |        NA |         NA |        NA |     ? |       ? |        NA |        NA |        NA |         NA |           ? |
| ConvertJsonlToCsv              | 100000 | 81.791 ms | 13.9049 ms | 3.6111 ms |  2.13 |    0.09 | 2000.0000 | 1000.0000 | 1000.0000 | 61571696 B |        3.60 |

Benchmarks with issues:
  JsonlBenchmark.ConvertCsvToJsonlFlat: Job-INMAZI(IterationCount=5, RunStrategy=Throughput, WarmupCount=3) [Rows=100000]
