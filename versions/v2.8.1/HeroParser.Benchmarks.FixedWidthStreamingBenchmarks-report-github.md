```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.22GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                                    | Rows   | Fields | Mean        | Error       | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------------------ |------- |------- |------------:|------------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ParseFromText**                             | **10000**  | **4**      |    **192.5 μs** |     **2.52 μs** |   **0.39 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 4      |    413.5 μs |     2.86 μs |   0.44 μs |  2.15 |    0.00 |    5.3711 |    0.9766 |    0.4883 |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 4      |    740.0 μs |   106.73 μs |  27.72 μs |  3.84 |    0.13 |    5.8594 |         - |         - |    70194 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 4      |  1,070.4 μs |    92.12 μs |  23.92 μs |  5.56 |    0.11 |  191.4063 |  111.3281 |   70.3125 |  2381478 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 4      |  1,260.4 μs |    77.32 μs |  11.96 μs |  6.55 |    0.06 |  205.0781 |  128.9063 |   72.2656 |  2385795 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 4      |  1,985.7 μs |   384.65 μs |  99.89 μs | 10.32 |    0.48 |  148.4375 |   78.1250 |   62.5000 |  2406256 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **10000**  | **8**      |    **193.6 μs** |     **0.98 μs** |   **0.25 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 8      |    486.7 μs |     3.65 μs |   0.95 μs |  2.51 |    0.01 |    4.8828 |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 8      |    901.8 μs |    44.06 μs |  11.44 μs |  4.66 |    0.05 |    7.8125 |         - |         - |    87281 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 8      |  2,392.2 μs |   218.36 μs |  33.79 μs | 12.35 |    0.16 |  375.0000 |  347.6563 |  179.6875 |  4066083 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 8      |  2,656.7 μs |   305.69 μs |  79.39 μs | 13.72 |    0.37 |  390.6250 |  367.1875 |  195.3125 |  4070836 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 8      |  3,310.7 μs |   550.05 μs | 142.85 μs | 17.10 |    0.67 |  328.1250 |  296.8750 |  179.6875 |  4093972 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **4**      |  **1,852.9 μs** |    **14.75 μs** |   **3.83 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 4      |  4,036.4 μs |   132.23 μs |  34.34 μs |  2.18 |    0.02 |         - |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 4      |  7,097.4 μs |   338.74 μs |  87.97 μs |  3.83 |    0.04 |   15.6250 |         - |         - |   230392 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 4      | 15,943.4 μs | 1,016.59 μs | 264.01 μs |  8.60 |    0.13 | 2156.2500 | 1312.5000 |  906.2500 | 23673221 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 4      | 17,286.3 μs | 1,857.68 μs | 482.43 μs |  9.33 |    0.24 | 2218.7500 | 1500.0000 |  968.7500 | 23677482 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 4      | 21,532.1 μs |   756.95 μs | 117.14 μs | 11.62 |    0.06 | 1968.7500 | 1281.2500 |  875.0000 | 23843177 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **8**      |  **1,939.4 μs** |    **11.21 μs** |   **1.74 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 8      |  4,542.0 μs |    47.84 μs |  12.42 μs |  2.34 |    0.01 |         - |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 8      |  7,851.9 μs |   291.04 μs |  75.58 μs |  4.05 |    0.04 |   31.2500 |         - |         - |   404105 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 8      | 19,041.1 μs | 2,742.58 μs | 424.42 μs |  9.82 |    0.20 | 2843.7500 | 1687.5000 |  968.7500 | 40509006 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 8      | 21,279.2 μs | 2,103.69 μs | 325.55 μs | 10.97 |    0.15 | 2968.7500 | 1781.2500 | 1000.0000 | 40512538 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 8      | 28,604.0 μs | 2,654.50 μs | 689.37 μs | 14.75 |    0.33 | 2031.2500 | 1312.5000 |  781.2500 | 40831728 B |          NA |
