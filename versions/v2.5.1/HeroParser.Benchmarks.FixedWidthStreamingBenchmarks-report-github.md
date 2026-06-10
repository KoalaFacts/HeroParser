```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                                    | Rows   | Fields | Mean        | Error       | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------------------ |------- |------- |------------:|------------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ParseFromText**                             | **10000**  | **4**      |    **209.9 μs** |     **0.25 μs** |   **0.06 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 4      |    421.3 μs |     2.63 μs |   0.68 μs |  2.01 |    0.00 |   18.5547 |    2.4414 |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 4      |  1,023.8 μs | 1,061.03 μs | 275.55 μs |  4.88 |    1.20 |         - |         - |         - |    70194 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 4      |  1,149.3 μs |   110.45 μs |  17.09 μs |  5.47 |    0.07 |  197.2656 |  117.1875 |   74.2188 |  2381480 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 4      |  1,323.7 μs |    98.73 μs |  25.64 μs |  6.31 |    0.11 |  201.1719 |  126.9531 |   74.2188 |  2385784 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 4      |  1,901.0 μs |   484.55 μs | 125.84 μs |  9.06 |    0.55 |  164.0625 |   93.7500 |   62.5000 |  2406268 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **10000**  | **8**      |    **217.0 μs** |     **1.49 μs** |   **0.23 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 10000  | 8      |    500.0 μs |     3.08 μs |   0.80 μs |  2.30 |    0.00 |    4.8828 |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 10000  | 8      |    960.3 μs |   196.06 μs |  30.34 μs |  4.43 |    0.13 |    7.8125 |         - |         - |    87346 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 10000  | 8      |  2,583.8 μs |    79.28 μs |  20.59 μs | 11.91 |    0.09 |  378.9063 |  359.3750 |  191.4063 |  4066356 B |          NA |
| ParseTypedFromBufferedFileStream          | 10000  | 8      |  2,859.7 μs |   290.62 μs |  44.97 μs | 13.18 |    0.19 |  382.8125 |  355.4688 |  187.5000 |  4070697 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 10000  | 8      |  3,426.7 μs |   349.65 μs |  90.80 μs | 15.79 |    0.38 |  343.7500 |  320.3125 |  171.8750 |  4094834 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **4**      |  **2,031.0 μs** |     **2.55 μs** |   **0.39 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 4      |  4,137.2 μs |    16.85 μs |   2.61 μs |  2.04 |    0.00 |         - |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 4      |  7,435.1 μs |   447.15 μs | 116.12 μs |  3.66 |    0.05 |   15.6250 |         - |         - |   230407 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 4      | 16,283.2 μs | 1,280.62 μs | 332.57 μs |  8.02 |    0.15 | 2062.5000 | 1343.7500 |  843.7500 | 23671900 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 4      | 18,090.3 μs | 1,389.06 μs | 360.74 μs |  8.91 |    0.16 | 2187.5000 | 1406.2500 |  937.5000 | 23676916 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 4      | 22,651.2 μs | 1,464.17 μs | 380.24 μs | 11.15 |    0.17 | 1968.7500 | 1343.7500 |  875.0000 | 23840911 B |          NA |
|                                           |        |        |             |             |           |       |         |           |           |           |            |             |
| **ParseFromText**                             | **100000** | **8**      |  **2,145.7 μs** |     **3.72 μs** |   **0.58 μs** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |          **-** |          **NA** |
| ParseFromAsyncStreamReader_MemoryStream   | 100000 | 8      |  4,911.4 μs |    37.45 μs |   9.73 μs |  2.29 |    0.00 |         - |         - |         - |    49648 B |          NA |
| ParseFromAsyncStreamReader_File           | 100000 | 8      |  9,462.7 μs |   785.16 μs | 121.50 μs |  4.41 |    0.05 |   31.2500 |         - |         - |   404130 B |          NA |
| ParseTypedFromBufferedStreamMemory        | 100000 | 8      | 20,710.9 μs | 3,724.49 μs | 967.24 μs |  9.65 |    0.41 | 2875.0000 | 1718.7500 | 1000.0000 | 40509560 B |          NA |
| ParseTypedFromBufferedFileStream          | 100000 | 8      | 22,936.1 μs | 2,258.10 μs | 349.44 μs | 10.69 |    0.15 | 2968.7500 | 1750.0000 | 1000.0000 | 40512930 B |          NA |
| ParseTypedFromBufferedFileAsyncEnumerable | 100000 | 8      | 31,551.9 μs | 3,333.66 μs | 865.74 μs | 14.70 |    0.37 | 1812.5000 | 1187.5000 |  625.0000 | 40829946 B |          NA |
