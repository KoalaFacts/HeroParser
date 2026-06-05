```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                                    | Rows   | Fields | Mean        | Error       | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------------------ |------- |------- |------------:|------------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ParseFromText**                             | **10000**  | **4**      |    **187.5 μs** |     **0.79 μs** |   **0.21 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 4      |    421.0 μs |     6.64 μs |   1.72 μs |  2.25 |    0.01 |   18.5547 |    2.4414 |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 4      |    845.1 μs |    21.71 μs |   5.64 μs |  4.51 |    0.03 |    6.8359 |         - |         - |    70192 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 4      |  1,065.0 μs |   124.53 μs |  32.34 μs |  5.68 |    0.16 |  197.2656 |  123.0469 |   72.2656 |  2381494 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 4      |  1,256.7 μs |    65.97 μs |  17.13 μs |  6.70 |    0.08 |  207.0313 |  130.8594 |   72.2656 |  2385795 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 4      |  1,853.5 μs |   520.64 μs | 135.21 μs |  9.89 |    0.66 |  179.6875 |  125.0000 |   62.5000 |  2406386 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **10000**  | **8**      |    **197.2 μs** |     **0.39 μs** |   **0.10 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 8      |    483.0 μs |     8.40 μs |   2.18 μs |  2.45 |    0.01 |   18.5547 |    2.4414 |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 8      |  1,142.5 μs |   276.52 μs |  42.79 μs |  5.80 |    0.19 |    7.8125 |         - |         - |    87284 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 8      |  2,342.7 μs |   273.74 μs |  71.09 μs | 11.88 |    0.33 |  382.8125 |  359.3750 |  191.4063 |  4066217 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 8      |  2,623.1 μs |   321.92 μs |  83.60 μs | 13.30 |    0.39 |  390.6250 |  351.5625 |  195.3125 |  4070730 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 8      |  3,212.3 μs |   423.73 μs | 110.04 μs | 16.29 |    0.51 |  367.1875 |  359.3750 |  179.6875 |  4094356 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **4**      |  **1,892.8 μs** |    **11.82 μs** |   **1.83 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 4      |  3,977.5 μs |     9.38 μs |   1.45 μs |  2.10 |    0.00 |         - |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 4      |  7,430.0 μs |   515.16 μs | 133.78 μs |  3.93 |    0.06 |   31.2500 |    7.8125 |    7.8125 |          - |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 4      | 15,272.2 μs | 1,227.45 μs | 189.95 μs |  8.07 |    0.09 | 2125.0000 | 1468.7500 |  875.0000 | 23672457 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 4      | 17,009.3 μs | 2,282.63 μs | 353.24 μs |  8.99 |    0.17 | 2187.5000 | 1437.5000 |  937.5000 | 23675920 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 4      | 21,882.2 μs | 2,009.64 μs | 521.90 μs | 11.56 |    0.25 | 1906.2500 | 1312.5000 |  843.7500 | 23841617 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **8**      |  **1,916.8 μs** |     **7.15 μs** |   **1.86 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 8      |  4,697.1 μs |    82.76 μs |  21.49 μs |  2.45 |    0.01 |         - |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 8      |  8,703.1 μs |   402.19 μs |  62.24 μs |  4.54 |    0.03 |   31.2500 |         - |         - |   404112 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 8      | 19,794.6 μs | 2,621.61 μs | 680.82 μs | 10.33 |    0.32 | 2812.5000 | 1593.7500 |  937.5000 | 40507526 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 8      | 21,800.0 μs | 2,832.13 μs | 735.50 μs | 11.37 |    0.35 | 2968.7500 | 1906.2500 | 1000.0000 | 40512250 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 8      | 28,696.2 μs | 3,103.92 μs | 480.33 μs | 14.97 |    0.22 | 1781.2500 | 1093.7500 |  656.2500 | 40826681 B |          NA |
