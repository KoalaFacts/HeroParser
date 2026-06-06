```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0      | Gen1     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|----------:|---------:|------:|--------:|----------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 52.75 ms | 15.920 ms | 4.134 ms |  1.00 |    0.10 | 1000.0000 | 333.3333 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 53.98 ms |  2.001 ms | 0.310 ms |  1.03 |    0.07 | 1000.0000 | 333.3333 |   15.9 MB |        1.11 |
