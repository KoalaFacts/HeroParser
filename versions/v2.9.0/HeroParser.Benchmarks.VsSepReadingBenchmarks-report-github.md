```

BenchmarkDotNet v0.16.0-preview.2, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD EPYC 9V74 3.62GHz, 1 CPU, 4 logical and 2 physical cores
Memory: 15.61 GB Total, 7.56 GB Available
.NET SDK 11.0.100
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                       | Rows  | Columns | WithQuotes | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------- |------ |-------- |----------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Sep**                          | **10000** | **25**      | **False**      |   **479.0 μs** |  **7.99 μs** |  **1.24 μs** |  **1.00** |    **0.00** |      **-** |    **1072 B** |        **1.00** |
| Sylvan                       | 10000 | 25      | False      |   636.3 μs | 16.85 μs |  4.38 μs |  1.33 |    0.01 | 3.9063 |   41633 B |       38.84 |
| CsvHelper                    | 10000 | 25      | False      |   589.3 μs | 32.14 μs |  8.35 μs |  1.23 |    0.02 |      - |     776 B |        0.72 |
| &#39;HeroParser UTF-16 (string)&#39; | 10000 | 25      | False      |   568.1 μs | 19.48 μs |  3.02 μs |  1.19 |    0.01 |      - |     176 B |        0.16 |
| &#39;HeroParser UTF-8 (byte[])&#39;  | 10000 | 25      | False      |   474.6 μs |  4.91 μs |  0.76 μs |  0.99 |    0.00 |      - |     176 B |        0.16 |
|                              |       |         |            |            |          |          |       |         |        |           |             |
| **Sep**                          | **10000** | **25**      | **True**       | **1,422.2 μs** | **76.74 μs** | **19.93 μs** |  **1.00** |    **0.00** |      **-** |    **1072 B** |        **1.00** |
| Sylvan                       | 10000 | 25      | True       | 3,947.6 μs | 18.51 μs |  2.86 μs |  2.78 |    0.04 |      - |   41818 B |       39.01 |
| CsvHelper                    | 10000 | 25      | True       | 1,357.6 μs | 71.26 μs | 11.03 μs |  0.95 |    0.01 |      - |     776 B |        0.72 |
| &#39;HeroParser UTF-16 (string)&#39; | 10000 | 25      | True       | 1,578.5 μs | 20.82 μs |  5.41 μs |  1.11 |    0.01 |      - |     176 B |        0.16 |
| &#39;HeroParser UTF-8 (byte[])&#39;  | 10000 | 25      | True       | 1,521.2 μs |  9.49 μs |  1.47 μs |  1.07 |    0.01 |      - |     176 B |        0.16 |
