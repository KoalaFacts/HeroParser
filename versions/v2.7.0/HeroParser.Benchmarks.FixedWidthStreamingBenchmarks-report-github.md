```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon 6973P-C 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                                    | Rows   | Fields | Mean        | Error       | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2     | Allocated  | Alloc Ratio |
|------------------------------------------ |------- |------- |------------:|------------:|----------:|------:|--------:|----------:|----------:|---------:|-----------:|------------:|
| **ParseFromText**                             | **10000**  | **4**      |    **133.9 μs** |     **3.22 μs** |   **0.50 μs** |  **1.00** |    **0.00** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 4      |    260.8 μs |    24.47 μs |   6.35 μs |  1.95 |    0.04 |    4.8828 |         - |        - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 4      |    537.5 μs |    16.40 μs |   2.54 μs |  4.01 |    0.02 |    6.8359 |         - |        - |    70192 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 4      |    846.4 μs |   113.19 μs |  17.52 μs |  6.32 |    0.12 |  198.2422 |  122.0703 |  66.4063 |  2381634 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 4      |    923.1 μs |    82.47 μs |  21.42 μs |  6.89 |    0.15 |  198.2422 |  120.1172 |  63.4766 |  2385969 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 4      |  1,283.1 μs |   170.31 μs |  44.23 μs |  9.58 |    0.30 |  191.4063 |  128.9063 |  66.4063 |  2406531 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |          |            |             |
| **ParseFromText**                             | **10000**  | **8**      |    **166.7 μs** |    **27.07 μs** |   **7.03 μs** |  **1.00** |    **0.05** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 8      |    306.0 μs |    14.09 μs |   2.18 μs |  1.84 |    0.07 |   18.5547 |    2.4414 |        - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 8      |    855.4 μs |    24.34 μs |   6.32 μs |  5.14 |    0.20 |    7.8125 |         - |        - |    87282 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 8      |  1,812.4 μs |   154.08 μs |  40.01 μs | 10.89 |    0.47 |  369.1406 |  337.8906 | 173.8281 |  4066143 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 8      |  1,942.0 μs |   349.54 μs |  90.77 μs | 11.67 |    0.66 |  371.0938 |  335.9375 | 171.8750 |  4070564 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 8      |  2,502.1 μs |   410.20 μs | 106.53 μs | 15.03 |    0.81 |  335.9375 |  335.9375 | 179.6875 |  4093636 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |          |            |             |
| **ParseFromText**                             | **100000** | **4**      |  **1,884.0 μs** |    **98.49 μs** |  **25.58 μs** |  **1.00** |    **0.02** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 4      |  2,539.9 μs |   267.07 μs |  69.36 μs |  1.35 |    0.04 |    3.9063 |         - |        - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 4      |  5,358.7 μs |   239.42 μs |  37.05 μs |  2.84 |    0.04 |   15.6250 |         - |        - |   230403 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 4      | 12,509.6 μs | 1,731.79 μs | 449.74 μs |  6.64 |    0.23 | 1968.7500 | 1125.0000 | 734.3750 | 23671617 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 4      | 13,186.5 μs | 1,278.53 μs | 332.03 μs |  7.00 |    0.18 | 2046.8750 | 1265.6250 | 781.2500 | 23676976 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 4      | 16,592.3 μs | 3,023.38 μs | 467.87 μs |  8.81 |    0.25 | 2000.0000 | 1312.5000 | 875.0000 | 23841692 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |          |            |             |
| **ParseFromText**                             | **100000** | **8**      |  **1,598.9 μs** |   **102.49 μs** |  **26.62 μs** |  **1.00** |    **0.02** |         **-** |         **-** |        **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 8      |  3,199.3 μs |    16.85 μs |   2.61 μs |  2.00 |    0.03 |    3.9063 |         - |        - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 8      |  8,465.6 μs |   156.10 μs |  24.16 μs |  5.30 |    0.08 |   31.2500 |         - |        - |   404127 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 8      | 18,728.2 μs | 3,047.22 μs | 791.35 μs | 11.72 |    0.49 | 2656.2500 | 1531.2500 | 875.0000 | 40508461 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 8      | 19,309.9 μs | 2,044.68 μs | 531.00 μs | 12.08 |    0.35 | 2781.2500 | 1687.5000 | 906.2500 | 40514630 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 8      | 23,805.6 μs | 2,651.87 μs | 410.38 μs | 14.89 |    0.32 | 1812.5000 | 1125.0000 | 656.2500 | 40826576 B |          NA |
