```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | Gen0      | Gen1     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|----------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 50.96 ms | 2.068 ms | 0.320 ms |  1.00 | 1000.0000 | 333.3333 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 56.60 ms | 2.413 ms | 0.627 ms |  1.11 | 1000.0000 | 333.3333 |   15.9 MB |        1.11 |
