```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                                    | Rows   | Fields | Mean        | Error       | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------------------ |------- |------- |------------:|------------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ParseFromText**                             | **10000**  | **4**      |    **213.2 μs** |     **0.60 μs** |   **0.09 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 4      |    426.2 μs |     5.51 μs |   0.85 μs |  2.00 |    0.00 |    5.8594 |    0.4883 |    0.4883 |          - |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 4      |    765.7 μs |    46.96 μs |  12.20 μs |  3.59 |    0.05 |    8.7891 |    0.9766 |         - |    70225 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 4      |  1,138.1 μs |    27.36 μs |   4.23 μs |  5.34 |    0.02 |  208.9844 |  132.8125 |   72.2656 |  2381463 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 4      |  1,355.4 μs |   104.57 μs |  27.16 μs |  6.36 |    0.12 |  197.2656 |  119.1406 |   72.2656 |  2385772 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 4      |  1,793.2 μs |   365.93 μs |  95.03 μs |  8.41 |    0.41 |  171.8750 |  109.3750 |   62.5000 |  2406566 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **10000**  | **8**      |    **217.6 μs** |     **1.32 μs** |   **0.34 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 8      |    486.5 μs |     5.12 μs |   1.33 μs |  2.24 |    0.01 |   18.5547 |    2.4414 |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 8      |    932.3 μs |    66.61 μs |  17.30 μs |  4.28 |    0.07 |    7.8125 |         - |         - |    87277 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 8      |  2,530.1 μs |   297.51 μs |  77.26 μs | 11.63 |    0.32 |  367.1875 |  347.6563 |  179.6875 |  4066036 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 8      |  2,905.8 μs |   161.14 μs |  41.85 μs | 13.35 |    0.18 |  375.0000 |  335.9375 |  183.5938 |  4070311 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 8      |  3,389.1 μs |   496.38 μs |  76.82 μs | 15.57 |    0.31 |  343.7500 |  304.6875 |  171.8750 |  4094154 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **4**      |  **2,097.0 μs** |     **6.01 μs** |   **1.56 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 4      |  4,190.8 μs |    58.74 μs |  15.25 μs |  2.00 |    0.01 |         - |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 4      |  7,462.0 μs |   511.65 μs | 132.87 μs |  3.56 |    0.06 |   23.4375 |         - |         - |   230669 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 4      | 15,751.3 μs | 1,476.32 μs | 383.40 μs |  7.51 |    0.17 | 2062.5000 | 1250.0000 |  812.5000 | 23672354 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 4      | 18,389.0 μs | 1,774.44 μs | 460.82 μs |  8.77 |    0.20 | 2125.0000 | 1312.5000 |  875.0000 | 23676961 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 4      | 22,232.4 μs |   618.33 μs |  95.69 μs | 10.60 |    0.04 | 2062.5000 | 1343.7500 |  937.5000 | 23843100 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **8**      |  **2,118.1 μs** |     **8.47 μs** |   **1.31 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 8      |  4,824.5 μs |    44.86 μs |  11.65 μs |  2.28 |    0.01 |         - |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 8      |  9,292.7 μs |   310.72 μs |  48.08 μs |  4.39 |    0.02 |   31.2500 |         - |         - |   404127 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 8      | 21,025.8 μs | 3,538.79 μs | 919.01 μs |  9.93 |    0.40 | 2687.5000 | 1531.2500 |  875.0000 | 40510482 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 8      | 23,147.4 μs | 2,683.01 μs | 696.77 μs | 10.93 |    0.30 | 2906.2500 | 1781.2500 | 1000.0000 | 40516056 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 8      | 30,305.5 μs | 4,705.69 μs | 728.21 μs | 14.31 |    0.31 | 1937.5000 | 1218.7500 |  718.7500 | 40831223 B |          NA |
