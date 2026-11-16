```

BenchmarkDotNet v0.16.0-preview.2, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
Memory: 15.61 GB Total, 7.55 GB Available
.NET SDK 11.0.100
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                       | Rows  | Columns | WithQuotes | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------- |------ |-------- |----------- |-----------:|----------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Sep**                          | **10000** | **25**      | **False**      |   **554.0 μs** |   **8.20 μs** |  **2.13 μs** |  **1.00** |    **0.00** |      **-** |     **312 B** |        **1.00** |
| Sylvan                       | 10000 | 25      | False      |   629.8 μs |   3.02 μs |  0.79 μs |  1.14 |    0.00 | 4.8828 |   41633 B |      133.44 |
| CsvHelper                    | 10000 | 25      | False      |   582.4 μs |   6.37 μs |  1.65 μs |  1.05 |    0.00 |      - |     776 B |        2.49 |
| &#39;HeroParser UTF-16 (string)&#39; | 10000 | 25      | False      |   527.5 μs |   1.21 μs |  0.19 μs |  0.95 |    0.00 |      - |     144 B |        0.46 |
| &#39;HeroParser UTF-8 (byte[])&#39;  | 10000 | 25      | False      |   864.9 μs |   3.74 μs |  0.97 μs |  1.56 |    0.01 |      - |     144 B |        0.46 |
|                              |       |         |            |            |           |          |       |         |        |           |             |
| **Sep**                          | **10000** | **25**      | **True**       | **1,272.2 μs** |  **26.92 μs** |  **6.99 μs** |  **1.00** |    **0.00** |      **-** |     **312 B** |        **1.00** |
| Sylvan                       | 10000 | 25      | True       | 3,955.2 μs | 181.50 μs | 28.09 μs |  3.11 |    0.03 |      - |   41818 B |      134.03 |
| CsvHelper                    | 10000 | 25      | True       | 1,337.3 μs |  48.19 μs |  7.46 μs |  1.05 |    0.01 |      - |     776 B |        2.49 |
| &#39;HeroParser UTF-16 (string)&#39; | 10000 | 25      | True       |   651.5 μs |   4.33 μs |  0.67 μs |  0.51 |    0.00 |      - |     144 B |        0.46 |
| &#39;HeroParser UTF-8 (byte[])&#39;  | 10000 | 25      | True       |   988.1 μs |   3.11 μs |  0.48 μs |  0.78 |    0.00 |      - |     144 B |        0.46 |
