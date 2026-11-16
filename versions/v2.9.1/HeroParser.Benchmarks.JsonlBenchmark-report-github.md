```

BenchmarkDotNet v0.16.0-preview.2, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD EPYC 9V74 3.64GHz, 1 CPU, 4 logical and 2 physical cores
Memory: 15.61 GB Total, 7.69 GB Available
.NET SDK 11.0.100
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2     | Allocated  | Alloc Ratio |
|------------------------------- |------- |----------:|----------:|----------:|------:|--------:|----------:|----------:|---------:|-----------:|------------:|
| **ReadFromText**                   | **10000**  |  **3.034 ms** | **0.1393 ms** | **0.0362 ms** |  **1.00** |    **0.00** |  **199.2188** |   **85.9375** |  **85.9375** |          **-** |          **NA** |
| ReadFromStream                 | 10000  |  2.838 ms | 0.1414 ms | 0.0367 ms |  0.94 |    0.02 |  101.5625 |    7.8125 |   7.8125 |   968550 B |          NA |
| ReadFromFileAsync              | 10000  |  5.301 ms | 0.7104 ms | 0.1845 ms |  1.75 |    0.06 |  179.6875 |         - |        - |  1877910 B |          NA |
| WriteToText                    | 10000  |  2.434 ms | 0.1394 ms | 0.0362 ms |  0.80 |    0.01 |  195.3125 |  195.3125 | 195.3125 |  2573166 B |          NA |
| WriteToStream                  | 10000  |  1.914 ms | 0.2463 ms | 0.0640 ms |  0.63 |    0.02 |   83.9844 |   83.9844 |  83.9844 |   476415 B |          NA |
| ReadFromText_SourceGenerated   | 10000  |  2.933 ms | 0.2288 ms | 0.0594 ms |  0.97 |    0.02 |  167.9688 |   74.2188 |  74.2188 |          - |          NA |
| ReadFromStream_SourceGenerated | 10000  |  2.609 ms | 0.1408 ms | 0.0218 ms |  0.86 |    0.01 |  101.5625 |    7.8125 |   7.8125 |   968533 B |          NA |
| WriteToText_SourceGenerated    | 10000  |  2.193 ms | 0.1718 ms | 0.0266 ms |  0.72 |    0.01 |  195.3125 |  195.3125 | 195.3125 |  2573118 B |          NA |
| WriteToStream_SourceGenerated  | 10000  |  1.590 ms | 0.0688 ms | 0.0179 ms |  0.52 |    0.01 |   64.4531 |   64.4531 |  64.4531 |   646805 B |          NA |
| ConvertCsvToJsonlFlat          | 10000  |  3.393 ms | 0.3788 ms | 0.0586 ms |  1.12 |    0.02 |  316.4063 |  292.9688 | 292.9688 |  3509482 B |          NA |
| ConvertJsonlToCsv              | 10000  |  5.893 ms | 0.6470 ms | 0.1680 ms |  1.94 |    0.05 |  375.0000 |  242.1875 | 140.6250 |          - |          NA |
|                                |        |           |           |           |       |         |           |           |          |            |             |
| **ReadFromText**                   | **100000** | **32.179 ms** | **2.6365 ms** | **0.6847 ms** |  **1.00** |    **0.00** | **1562.5000** |  **375.0000** | **375.0000** |          **-** |          **NA** |
| ReadFromStream                 | 100000 | 28.476 ms | 0.2544 ms | 0.0661 ms |  0.89 |    0.02 | 1000.0000 |         - |        - | 10328520 B |          NA |
| ReadFromFileAsync              | 100000 | 57.067 ms | 5.4891 ms | 1.4255 ms |  1.77 |    0.05 | 1888.8889 |         - |        - | 19878144 B |          NA |
| WriteToText                    | 100000 | 23.964 ms | 1.1479 ms | 0.2981 ms |  0.74 |    0.02 |  406.2500 |  406.2500 | 406.2500 | 26310278 B |          NA |
| WriteToStream                  | 100000 | 19.289 ms | 1.2839 ms | 0.3334 ms |  0.60 |    0.01 |  187.5000 |  187.5000 | 187.5000 |  6756277 B |          NA |
| ReadFromText_SourceGenerated   | 100000 | 32.458 ms | 1.5621 ms | 0.4057 ms |  1.01 |    0.02 | 1937.5000 |  937.5000 | 937.5000 | 17084127 B |          NA |
| ReadFromStream_SourceGenerated | 100000 | 27.247 ms | 2.3591 ms | 0.6126 ms |  0.85 |    0.02 |  687.5000 |         - |        - | 10328520 B |          NA |
| WriteToText_SourceGenerated    | 100000 | 20.602 ms | 0.7066 ms | 0.1093 ms |  0.64 |    0.01 |  375.0000 |  375.0000 | 375.0000 | 26309860 B |          NA |
| WriteToStream_SourceGenerated  | 100000 | 15.071 ms | 0.8847 ms | 0.2297 ms |  0.47 |    0.01 |  156.2500 |  156.2500 | 156.2500 |          - |          NA |
| ConvertCsvToJsonlFlat          | 100000 | 32.335 ms | 3.0135 ms | 0.7826 ms |  1.01 |    0.03 |  562.5000 |  562.5000 | 562.5000 | 31487496 B |          NA |
| ConvertJsonlToCsv              | 100000 | 62.224 ms | 3.4366 ms | 0.5318 ms |  1.93 |    0.04 | 1555.5556 | 1222.2222 | 777.7778 | 61568900 B |          NA |
