```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|--------:|----------:|---------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 48.36 ms | 1.408 ms | 0.218 ms |  1.00 |    0.01 | 1181.8182 | 454.5455 | 181.8182 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 53.54 ms | 5.368 ms | 1.394 ms |  1.11 |    0.03 | 1000.0000 | 333.3333 |        - |   15.9 MB |        1.11 |
