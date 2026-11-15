```

BenchmarkDotNet v0.16.0-preview.2, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
Memory: 15.61 GB Total, 7.49 GB Available
.NET SDK 11.0.100
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                                    | Rows   | Fields | Mean        | Error       | StdDev      | Ratio | RatioSD | Gen0      | Gen1      | Gen2     | Allocated  | Alloc Ratio |
|------------------------------------------ |------- |------- |------------:|------------:|------------:|------:|--------:|----------:|----------:|---------:|-----------:|------------:|
| **ParseFromText**                             | **10000**  | **4**      |    **160.8 μs** |     **2.31 μs** |     **0.36 μs** |  **1.00** |    **0.00** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 4      |    385.5 μs |    19.16 μs |     2.97 μs |  2.40 |    0.02 |    4.8828 |         - |        - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 4      |    567.1 μs |    13.03 μs |     3.38 μs |  3.53 |    0.02 |    6.8359 |         - |        - |    70138 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 4      |    997.7 μs |   104.78 μs |    27.21 μs |  6.20 |    0.16 |  183.5938 |  117.1875 |  74.2188 |  2381652 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 4      |  1,119.0 μs |    76.06 μs |    11.77 μs |  6.96 |    0.07 |  175.7813 |  111.3281 |  76.1719 |  2385920 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 4      |  1,395.9 μs |    92.36 μs |    23.99 μs |  8.68 |    0.14 |  171.8750 |  126.9531 |  66.4063 |  2406653 B |          NA |
|                                           |        |        |             |             |             |       |         |           |           |          |            |             |
| **ParseFromText**                             | **10000**  | **8**      |    **172.9 μs** |     **2.53 μs** |     **0.39 μs** |  **1.00** |    **0.00** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 8      |    402.2 μs |    17.96 μs |     2.78 μs |  2.33 |    0.02 |    4.8828 |         - |        - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 8      |    708.9 μs |    13.93 μs |     3.62 μs |  4.10 |    0.02 |    7.8125 |         - |        - |    87223 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 8      |  1,996.5 μs |   297.23 μs |    77.19 μs | 11.55 |    0.41 |  328.1250 |  296.8750 | 164.0625 |  4066143 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 8      |  2,226.1 μs |   343.68 μs |    89.25 μs | 12.87 |    0.47 |  328.1250 |  281.2500 | 164.0625 |  4070290 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 8      |  2,800.2 μs |   333.44 μs |    86.59 μs | 16.19 |    0.46 |  320.3125 |  296.8750 | 160.1563 |  4093140 B |          NA |
|                                           |        |        |             |             |             |       |         |           |           |          |            |             |
| **ParseFromText**                             | **100000** | **4**      |  **1,625.8 μs** |    **10.49 μs** |     **2.72 μs** |  **1.00** |    **0.00** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 4      |  3,403.7 μs |    42.59 μs |    11.06 μs |  2.09 |    0.01 |    3.9063 |         - |        - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 4      |  5,450.8 μs |   176.06 μs |    45.72 μs |  3.35 |    0.03 |   15.6250 |         - |        - |   230344 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 4      | 16,012.6 μs | 2,011.10 μs |   522.28 μs |  9.85 |    0.29 | 1781.2500 | 1031.2500 | 750.0000 | 23672246 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 4      | 16,968.6 μs | 1,059.59 μs |   275.17 μs | 10.44 |    0.16 | 1968.7500 | 1343.7500 | 906.2500 | 23678261 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 4      | 19,904.5 μs | 1,497.81 μs |   388.98 μs | 12.24 |    0.22 | 1875.0000 | 1468.7500 | 875.0000 | 23840783 B |          NA |
|                                           |        |        |             |             |             |       |         |           |           |          |            |             |
| **ParseFromText**                             | **100000** | **8**      |  **1,685.4 μs** |    **27.28 μs** |     **7.08 μs** |  **1.00** |    **0.00** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 8      |  4,027.9 μs |   227.81 μs |    59.16 μs |  2.39 |    0.03 |         - |         - |        - |    49656 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 8      |  6,864.5 μs |   330.35 μs |    51.12 μs |  4.07 |    0.03 |   39.0625 |         - |        - |   404075 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 8      | 23,882.5 μs | 3,301.99 μs |   857.52 μs | 14.17 |    0.47 | 2500.0000 | 1531.2500 | 875.0000 | 40509171 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 8      | 26,178.5 μs | 6,269.12 μs | 1,628.07 μs | 15.53 |    0.88 | 2562.5000 | 1625.0000 | 937.5000 | 40513728 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 8      | 30,885.3 μs | 3,302.78 μs |   511.11 μs | 18.33 |    0.28 | 2031.2500 | 1218.7500 | 781.2500 | 40830722 B |          NA |
