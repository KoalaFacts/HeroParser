```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon 6973P-C 3.70GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|--------:|----------:|---------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 32.66 ms | 2.173 ms | 0.336 ms |  1.00 |    0.01 | 1250.0000 | 500.0000 | 250.0000 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 36.22 ms | 4.605 ms | 1.196 ms |  1.11 |    0.04 | 1214.2857 | 428.5714 | 142.8571 |   15.9 MB |        1.11 |
