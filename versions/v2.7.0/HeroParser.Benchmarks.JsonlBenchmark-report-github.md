```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon 6973P-C 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1     | Gen2     | Allocated  | Alloc Ratio |
|------------------------------- |------- |----------:|----------:|----------:|------:|--------:|----------:|---------:|---------:|-----------:|------------:|
| **ReadFromText**                   | **10000**  |  **2.477 ms** | **0.0560 ms** | **0.0087 ms** |  **1.00** |    **0.00** |  **277.3438** |  **97.6563** |  **97.6563** |          **-** |          **NA** |
| ReadFromStream                 | 10000  |  2.210 ms | 0.1327 ms | 0.0345 ms |  0.89 |    0.01 |   89.8438 |        - |        - |   968520 B |          NA |
| ReadFromFileAsync              | 10000  |  5.069 ms | 0.4774 ms | 0.0739 ms |  2.05 |    0.03 |  250.0000 |        - |        - |  1878144 B |          NA |
| WriteToText                    | 10000  |  1.982 ms | 0.1216 ms | 0.0316 ms |  0.80 |    0.01 |  191.4063 | 191.4063 | 191.4063 |  2573025 B |          NA |
| WriteToStream                  | 10000  |  1.486 ms | 0.1530 ms | 0.0397 ms |  0.60 |    0.01 |   74.2188 |  74.2188 |  74.2188 |          - |          NA |
| ReadFromText_SourceGenerated   | 10000  |  1.865 ms | 0.0709 ms | 0.0110 ms |  0.75 |    0.00 |  150.3906 |  83.9844 |  83.9844 |          - |          NA |
| ReadFromStream_SourceGenerated | 10000  |  1.528 ms | 0.0422 ms | 0.0065 ms |  0.62 |    0.00 |   46.8750 |        - |        - |   488520 B |          NA |
| WriteToText_SourceGenerated    | 10000  |  1.684 ms | 0.2629 ms | 0.0683 ms |  0.68 |    0.03 |  173.8281 | 173.8281 | 173.8281 |  2572729 B |          NA |
| WriteToStream_SourceGenerated  | 10000  |  1.128 ms | 0.0892 ms | 0.0232 ms |  0.46 |    0.01 |   58.5938 |  58.5938 |  58.5938 |   646580 B |          NA |
| ConvertCsvToJsonlFlat          | 10000  |  2.716 ms | 0.5754 ms | 0.1494 ms |  1.10 |    0.06 |  308.5938 | 289.0625 | 289.0625 |  3509228 B |          NA |
| ConvertJsonlToCsv              | 10000  |  5.296 ms | 0.4111 ms | 0.0636 ms |  2.14 |    0.02 |  375.0000 | 218.7500 | 132.8125 |          - |          NA |
|                                |        |           |           |           |       |         |           |          |          |            |             |
| **ReadFromText**                   | **100000** | **27.287 ms** | **4.8597 ms** | **1.2620 ms** |  **1.00** |    **0.06** | **1906.2500** | **875.0000** | **875.0000** | **17084038 B** |        **1.00** |
| ReadFromStream                 | 100000 | 22.675 ms | 0.5817 ms | 0.0900 ms |  0.83 |    0.04 | 1062.5000 |  62.5000 |  62.5000 | 10328562 B |        0.60 |
| ReadFromFileAsync              | 100000 | 53.005 ms | 7.1693 ms | 1.8619 ms |  1.95 |    0.10 | 1666.6667 |        - |        - | 19878368 B |        1.16 |
| WriteToText                    | 100000 | 17.120 ms | 0.7268 ms | 0.1887 ms |  0.63 |    0.03 |  375.0000 | 375.0000 | 375.0000 | 26310156 B |        1.54 |
| WriteToStream                  | 100000 | 13.485 ms | 0.5907 ms | 0.1534 ms |  0.50 |    0.02 |  187.5000 | 187.5000 | 187.5000 |  6755832 B |        0.40 |
| ReadFromText_SourceGenerated   | 100000 | 18.308 ms | 0.3017 ms | 0.0784 ms |  0.67 |    0.03 |  937.5000 | 468.7500 | 468.7500 | 11563163 B |        0.68 |
| ReadFromStream_SourceGenerated | 100000 | 15.699 ms | 0.1771 ms | 0.0460 ms |  0.58 |    0.02 |  468.7500 |        - |        - |  4808520 B |        0.28 |
| WriteToText_SourceGenerated    | 100000 | 13.815 ms | 0.6934 ms | 0.1801 ms |  0.51 |    0.02 |  359.3750 | 359.3750 | 359.3750 | 26310502 B |        1.54 |
| WriteToStream_SourceGenerated  | 100000 | 11.049 ms | 1.2292 ms | 0.3192 ms |  0.41 |    0.02 |  187.5000 | 187.5000 | 187.5000 |  6755838 B |        0.40 |
| ConvertCsvToJsonlFlat          | 100000 |        NA |        NA |        NA |     ? |       ? |        NA |       NA |       NA |         NA |           ? |
| ConvertJsonlToCsv              | 100000 | 57.849 ms | 1.6324 ms | 0.4239 ms |  2.12 |    0.09 | 1000.0000 | 500.0000 | 500.0000 | 61567584 B |        3.60 |

Benchmarks with issues:
  JsonlBenchmark.ConvertCsvToJsonlFlat: Job-INMAZI(IterationCount=5, RunStrategy=Throughput, WarmupCount=3) [Rows=100000]
