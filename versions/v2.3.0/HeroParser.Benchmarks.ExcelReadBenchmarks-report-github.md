```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|----------:|---------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 48.64 ms | 1.794 ms | 0.278 ms |  1.00 | 1363.6364 | 545.4545 | 272.7273 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 52.04 ms | 2.635 ms | 0.684 ms |  1.07 | 1000.0000 | 333.3333 |        - |   15.9 MB |        1.11 |
