# Roundtrip baseline — before the Block 8 performance findings

Measured **before** any of the P-1/P-2/P-4/P-6/P-7 fixes, so their effect can be judged
against these numbers. Reproduce with:

```
dotnet run --project src/benchmarks/Cocoar.SignalARRR.Benchmarks -c Release -- --filter "*RoundtripBenchmarks*"
```

## Environment

- Code: `develop @ 074015a` (plus the benchmark project itself)
- BenchmarkDotNet v0.15.8, DefaultJob, `[MemoryDiagnoser]`
- Windows 11 (10.0.26200), 13th Gen Intel Core i9-13950HX, 32 logical / 24 physical cores
- .NET SDK 10.0.302 · runtime .NET 10.0.10, X64 RyuJIT x86-64-v3 · GC = Concurrent Server
- In-process Kestrel on loopback (`127.0.0.1`), one WebSocket connection, JSON protocol,
  no telemetry listener (production default)

## Results (2026-08-07)

| Method                   | Mean     | StdDev   | Median   | Allocated |
|------------------------- |---------:|---------:|---------:|----------:|
| HubMethod_0Args          | 240.7 µs | 25.2 µs  | 237.8 µs |   9.30 KB |
| HubMethod_1Arg           | 297.8 µs | 27.5 µs  | 297.3 µs |   9.94 KB |
| HubMethod_3Args          | 298.2 µs | 54.5 µs  | 280.0 µs |  10.68 KB |
| ServerMethodsClass_0Args | 265.0 µs | 47.8 µs  | 263.5 µs |  10.06 KB |
| ServerMethodsClass_1Arg  | 287.3 µs | 37.4 µs  |        — |  10.70 KB |
| ServerMethodsClass_3Args | 263.1 µs | 47.2 µs  | 258.5 µs |  11.44 KB |

`ServerMethodsClass_1Arg` is from a standalone re-run: in the full-suite run its process
died mid-measurement (see below). The other five values are from the full-suite run.

## How to read these numbers

- The mean is dominated by loopback WebSocket transport and JSON serialization; the
  framework overhead the Block 8 findings target (reflection property sets, logger
  creation, auth-plan and parameter-plan resolution) is a slice of it. **Allocated is the
  more sensitive column** for the cache fixes — watch it as much as the mean.
- Distributions are multimodal (BenchmarkDotNet flags mValue up to 4.3) and StdDev is
  10–20 % of the mean — loopback networking on a busy desktop is noisy. Treat differences
  of a few percent as noise; the P-fixes are expected to show up primarily in Allocated
  and only secondarily in Mean.
- Per-benchmark processes each start their own server and connection, so the six rows are
  independent measurements.

## Observed once, not reproducible

In the full-suite run, `ServerMethodsClass_1Arg` failed at measurement iteration 73 (after
~150,000 successful calls in that process): the server closed the SignalR connection with
"Connection closed with an error." An immediate standalone re-run of the same benchmark
completed all iterations cleanly — one occurrence in roughly 1.2 million calls across the
suite. Worth remembering if a sustained-load connection drop ever shows up again; nothing
was filed for it.
