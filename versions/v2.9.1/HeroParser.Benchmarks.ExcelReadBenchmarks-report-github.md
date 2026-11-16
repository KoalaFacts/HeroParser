```

BenchmarkDotNet v0.16.0-preview.2, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD EPYC 9V74 3.68GHz, 1 CPU, 4 logical and 2 physical cores
Memory: 15.61 GB Total, 7.6 GB Available
.NET SDK 11.0.100
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|--------:|----------:|---------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 39.51 ms | 2.177 ms | 0.565 ms |  1.00 |    0.00 | 1153.8462 | 461.5385 | 230.7692 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 40.79 ms | 2.061 ms | 0.319 ms |  1.03 |    0.02 | 1076.9231 | 384.6154 | 153.8462 |  14.07 MB |        0.98 |
