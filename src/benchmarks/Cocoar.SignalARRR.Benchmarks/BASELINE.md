# Roundtrip baseline — before/after the Block 8 performance findings

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

## After the P-1/P-4/P-6/P-7 fixes (2026-08-07, same machine and environment)

| Method                   | Mean     | StdDev   | Median   | Allocated |
|------------------------- |---------:|---------:|---------:|----------:|
| HubMethod_0Args          | 230.3 µs | 28.5 µs  | 225.6 µs |   9.29 KB |
| HubMethod_1Arg           | 157.9 µs | 74.4 µs  | 118.4 µs |   9.85 KB |
| HubMethod_3Args          | 135.2 µs | 32.3 µs  | 129.9 µs |  10.47 KB |
| ServerMethodsClass_0Args | 117.2 µs | 22.9 µs  | 111.9 µs |   9.43 KB |
| ServerMethodsClass_1Arg  | 113.2 µs | 26.6 µs  | 109.3 µs |   9.98 KB |
| ServerMethodsClass_3Args | 103.9 µs |  6.8 µs  | 102.8 µs |  10.59 KB |

Reading the delta:

- **ServerMethods roundtrips dropped to ~0.4–0.5× of baseline** (263–287 µs → 104–117 µs) —
  this is the path that carried all of the removed work: two `CreateLogger` calls, five
  name-based reflective property sets, and the per-parameter reflection.
- **Hub-declared methods with arguments halved** (298 µs → 135–158 µs); the 0-args hub
  method barely moved (241 → 230 µs), consistent with it never having taken the
  property-set path and binding zero parameters.
- Allocations fell modestly (up to 0.9 KB per call) — the dominant win was time, not
  memory: the removed work was lock waits and reflection, not primarily allocation.
- The full suite now reaches BenchmarkDotNet's target precision in 4 minutes instead of
  29 — the run-to-run variance itself was largely the removed contention.
- `HubMethod_1Arg`'s after-mean carries a large StdDev (74 µs, bimodal); its median
  (118 µs) is the more representative number for that row.

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

## P-5 (compiled invokers) — measured, then deliberately not taken

The report ordered P-5 last, "only with numbers". `InvokerBenchmarks` isolates exactly the
slice P-5 would change — one method invocation, no server, no transport (2026-08-07, same
machine):

| Invocation path                        | Mean     | Allocated |
|--------------------------------------- |---------:|----------:|
| Reflectensions `InvokeHelper`, sync    | 2 033 ns |   1 032 B |
| Reflectensions `InvokeHelper`, async   | 1 160 ns |   1 024 B |
| Raw `MethodInfo.Invoke`, sync          |    80 ns |     112 B |
| Raw `MethodInfo.Invoke`, async + await |   136 ns |     256 B |
| Compiled delegate, sync                |    37 ns |     112 B |
| Compiled delegate, async + await       |    60 ns |     256 B |
| Direct call (includes a `Guid.Parse`)  |    50 ns |     112 B |

What the numbers say:

- The current path (Reflectensions) costs **~1–2 µs and ~1 KB per invocation**; the sync row
  carries its documented `Task.Run` wrap. Compiled invokers would remove nearly all of it.
- But the roundtrip this sits in measures **104–230 µs with 7–28 µs StdDev** — a 2 µs
  improvement is an order of magnitude below the noise floor. The roundtrip benchmark could
  never verify the fix it is supposed to justify.
- Notable: on modern .NET, raw `MethodInfo.Invoke` is only ~80 ns — the runtime grew invoke
  fast paths. If this is ever revisited, replacing `InvokeHelper` with plain
  `MethodInfo.Invoke` plus an own Task-unwrap captures ~96 % of the win without any
  compiled-invoker machinery.

**Decision: not taken.** Per the report's own rule ("only with numbers"), the numbers do not
justify a rewrite of the dispatch core today. Revisit if a profile ever shows invocation
overhead or the per-call ~1 KB mattering at real load — the micro-benchmark stays in the
suite as the measuring stick.

## Observed once, not reproducible

In the full-suite run, `ServerMethodsClass_1Arg` failed at measurement iteration 73 (after
~150,000 successful calls in that process): the server closed the SignalR connection with
"Connection closed with an error." An immediate standalone re-run of the same benchmark
completed all iterations cleanly — one occurrence in roughly 1.2 million calls across the
suite. Worth remembering if a sustained-load connection drop ever shows up again; nothing
was filed for it.
