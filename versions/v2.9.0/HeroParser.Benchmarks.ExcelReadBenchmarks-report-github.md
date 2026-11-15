```

BenchmarkDotNet v0.16.0-preview.2, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD EPYC 9V74 3.67GHz, 1 CPU, 4 logical and 2 physical cores
Memory: 15.61 GB Total, 7.85 GB Available
.NET SDK 11.0.100
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|----------:|---------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 39.22 ms | 1.678 ms | 0.260 ms |  1.00 | 1153.8462 | 461.5385 | 230.7692 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 40.96 ms | 0.853 ms | 0.132 ms |  1.04 | 1230.7692 | 461.5385 | 230.7692 |   15.9 MB |        1.11 |
