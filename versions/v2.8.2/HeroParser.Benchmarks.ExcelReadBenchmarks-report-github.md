```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.83GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|----------:|---------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 53.84 ms | 1.570 ms | 0.408 ms |  1.00 | 1300.0000 | 500.0000 | 200.0000 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 57.56 ms | 1.779 ms | 0.462 ms |  1.07 | 1000.0000 | 333.3333 |        - |   15.9 MB |        1.11 |
