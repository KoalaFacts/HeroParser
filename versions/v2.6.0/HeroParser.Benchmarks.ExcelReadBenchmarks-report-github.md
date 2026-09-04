```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 9V74 3.68GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v4
  Job-INMAZI : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v4

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|----------:|---------:|---------:|----------:|------------:|
| ReadWithGeneratedCharBinder       | 37.30 ms | 0.929 ms | 0.241 ms |  1.00 | 1285.7143 | 500.0000 | 214.2857 |  14.32 MB |        1.00 |
| ReadWithFallbackCharToByteAdapter | 40.54 ms | 2.022 ms | 0.313 ms |  1.09 | 1000.0000 | 333.3333 |        - |   15.9 MB |        1.11 |
