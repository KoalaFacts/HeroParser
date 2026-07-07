```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0      | Gen1     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|--------:|----------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 50.86 ms | 3.965 ms | 1.030 ms |  1.00 |    0.03 | 1000.0000 | 333.3333 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 54.72 ms | 1.739 ms | 0.269 ms |  1.08 |    0.02 | 1000.0000 | 333.3333 |   15.9 MB |        1.11 |
