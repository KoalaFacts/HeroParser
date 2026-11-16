```

BenchmarkDotNet v0.16.0-preview.2, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
Memory: 15.61 GB Total, 7.32 GB Available
.NET SDK 11.0.100
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                                    | Rows   | Fields | Mean        | Error       | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2     | Allocated  | Alloc Ratio |
|------------------------------------------ |------- |------- |------------:|------------:|----------:|------:|--------:|----------:|----------:|---------:|-----------:|------------:|
| **ParseFromText**                             | **10000**  | **4**      |    **164.1 μs** |     **0.55 μs** |   **0.08 μs** |  **1.00** |    **0.00** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 4      |    348.9 μs |     5.47 μs |   1.42 μs |  2.13 |    0.01 |    4.8828 |    0.4883 |        - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 4      |    587.2 μs |     9.93 μs |   2.58 μs |  3.58 |    0.01 |    6.8359 |         - |        - |    70137 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 4      |    946.0 μs |    62.80 μs |  16.31 μs |  5.76 |    0.09 |  185.5469 |  119.1406 |  74.2188 |  2381675 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 4      |  1,094.3 μs |   144.67 μs |  37.57 μs |  6.67 |    0.21 |  183.5938 |  117.1875 |  74.2188 |  2386031 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 4      |  1,422.6 μs |   147.65 μs |  38.34 μs |  8.67 |    0.21 |  166.0156 |  119.1406 |  68.3594 |  2406503 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |          |            |             |
| **ParseFromText**                             | **10000**  | **8**      |    **171.8 μs** |     **1.18 μs** |   **0.18 μs** |  **1.00** |    **0.00** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 8      |    407.8 μs |     2.67 μs |   0.41 μs |  2.37 |    0.00 |    6.8359 |    0.4883 |        - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 8      |    722.0 μs |    61.05 μs |  15.85 μs |  4.20 |    0.08 |    9.7656 |    1.9531 |   0.9766 |    87232 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 8      |  1,942.5 μs |   116.38 μs |  30.22 μs | 11.31 |    0.16 |  324.2188 |  300.7813 | 160.1563 |  4066008 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 8      |  2,199.1 μs |   122.69 μs |  31.86 μs | 12.80 |    0.17 |  328.1250 |  296.8750 | 164.0625 |  4070437 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 8      |  2,803.8 μs |   111.14 μs |  28.86 μs | 16.32 |    0.15 |  320.3125 |  312.5000 | 160.1563 |  4093467 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |          |            |             |
| **ParseFromText**                             | **100000** | **4**      |  **1,611.1 μs** |    **22.09 μs** |   **5.74 μs** |  **1.00** |    **0.00** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 4      |  3,402.6 μs |    84.36 μs |  21.91 μs |  2.11 |    0.01 |    3.9063 |         - |        - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 4      |  5,598.4 μs |   235.81 μs |  61.24 μs |  3.47 |    0.04 |   23.4375 |         - |        - |   230345 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 4      | 15,807.3 μs |   557.00 μs | 144.65 μs |  9.81 |    0.09 | 1812.5000 | 1093.7500 | 765.6250 | 23672434 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 4      | 16,417.5 μs |   764.79 μs | 118.35 μs | 10.19 |    0.07 | 1937.5000 | 1343.7500 | 875.0000 | 23677544 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 4      | 20,412.2 μs | 1,207.84 μs | 313.67 μs | 12.67 |    0.18 | 1875.0000 | 1437.5000 | 875.0000 | 23847178 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |          |            |             |
| **ParseFromText**                             | **100000** | **8**      |  **1,674.4 μs** |     **3.21 μs** |   **0.50 μs** |  **1.00** |    **0.00** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 8      |  4,030.8 μs |   129.95 μs |  33.75 μs |  2.41 |    0.02 |         - |         - |        - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 8      |  6,855.3 μs |   224.94 μs |  58.42 μs |  4.09 |    0.03 |   39.0625 |         - |        - |   404072 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 8      | 23,999.4 μs | 3,294.66 μs | 855.61 μs | 14.33 |    0.47 | 2562.5000 | 1812.5000 | 937.5000 | 40510257 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 8      | 26,090.9 μs | 2,262.02 μs | 587.44 μs | 15.58 |    0.32 | 2625.0000 | 1656.2500 | 968.7500 | 40514326 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 8      | 31,573.9 μs | 3,843.68 μs | 998.19 μs | 18.86 |    0.55 | 1812.5000 | 1187.5000 | 718.7500 | 40829468 B |          NA |
