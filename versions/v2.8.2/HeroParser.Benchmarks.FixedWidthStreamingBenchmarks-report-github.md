```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.61GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                                    | Rows   | Fields | Mean        | Error       | StdDev      | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------------------ |------- |------- |------------:|------------:|------------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ParseFromText**                             | **10000**  | **4**      |    **188.5 μs** |     **1.60 μs** |     **0.25 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 4      |    428.1 μs |     4.66 μs |     1.21 μs |  2.27 |    0.01 |    6.8359 |    0.9766 |         - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 4      |    742.5 μs |    30.60 μs |     7.95 μs |  3.94 |    0.04 |    6.8359 |         - |         - |    70201 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 4      |  1,150.0 μs |    68.95 μs |    10.67 μs |  6.10 |    0.05 |  203.1250 |  125.0000 |   72.2656 |  2381522 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 4      |  1,340.6 μs |   138.25 μs |    35.90 μs |  7.11 |    0.17 |  205.0781 |  132.8125 |   72.2656 |  2385842 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 4      |  1,825.2 μs |   401.83 μs |   104.35 μs |  9.68 |    0.51 |  179.6875 |  132.8125 |   62.5000 |  2406384 B |          NA |
|                                           |        |        |             |             |             |       |         |           |           |           |            |             |
| **ParseFromText**                             | **10000**  | **8**      |    **198.0 μs** |     **0.69 μs** |     **0.18 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 8      |    503.0 μs |     4.17 μs |     1.08 μs |  2.54 |    0.01 |    5.8594 |         - |         - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 8      |    896.0 μs |    60.64 μs |     9.38 μs |  4.53 |    0.04 |    7.8125 |         - |         - |    87287 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 8      |  2,596.6 μs |   379.53 μs |    98.56 μs | 13.11 |    0.45 |  375.0000 |  328.1250 |  179.6875 |  4066038 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 8      |  2,835.0 μs |   356.60 μs |    55.18 μs | 14.32 |    0.25 |  382.8125 |  339.8438 |  187.5000 |  4070736 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 8      |  3,489.9 μs |   639.56 μs |   166.09 μs | 17.63 |    0.77 |  359.3750 |  335.9375 |  179.6875 |  4094416 B |          NA |
|                                           |        |        |             |             |             |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **4**      |  **1,935.6 μs** |    **16.37 μs** |     **4.25 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 4      |  4,145.1 μs |    43.26 μs |     6.69 μs |  2.14 |    0.01 |         - |         - |         - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 4      |  7,254.5 μs |   355.10 μs |    92.22 μs |  3.75 |    0.04 |   31.2500 |         - |         - |   230417 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 4      | 20,303.3 μs | 3,032.64 μs |   787.57 μs | 10.49 |    0.37 | 2093.7500 | 1375.0000 |  875.0000 | 23671636 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 4      | 21,680.4 μs | 2,421.61 μs |   628.88 μs | 11.20 |    0.30 | 2187.5000 | 1406.2500 |  937.5000 | 23677175 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 4      | 25,299.4 μs | 2,402.78 μs |   371.83 μs | 13.07 |    0.17 | 2000.0000 | 1312.5000 |  875.0000 | 23841516 B |          NA |
|                                           |        |        |             |             |             |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **8**      |  **1,930.9 μs** |    **35.04 μs** |     **5.42 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 8      |  4,765.8 μs |   158.71 μs |    41.22 μs |  2.47 |    0.02 |         - |         - |         - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 8      |  9,405.0 μs | 1,282.03 μs |   198.40 μs |  4.87 |    0.09 |   31.2500 |         - |         - |   404142 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 8      | 27,101.8 μs | 3,164.52 μs |   489.71 μs | 14.04 |    0.23 | 2937.5000 | 1875.0000 | 1000.0000 | 40506706 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 8      | 29,144.5 μs | 3,005.02 μs |   780.39 μs | 15.09 |    0.37 | 2906.2500 | 1718.7500 |  968.7500 | 40513695 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 8      | 36,881.2 μs | 4,738.11 μs | 1,230.47 μs | 19.10 |    0.59 | 1714.2857 | 1142.8571 |  642.8571 | 40825795 B |          NA |
