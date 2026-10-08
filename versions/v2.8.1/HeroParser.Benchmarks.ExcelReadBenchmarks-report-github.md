```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | Gen0      | Gen1     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|----------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 52.23 ms | 1.863 ms | 0.484 ms |  1.00 | 1000.0000 | 333.3333 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 55.56 ms | 3.908 ms | 0.605 ms |  1.06 | 1000.0000 | 333.3333 |   15.9 MB |        1.11 |
