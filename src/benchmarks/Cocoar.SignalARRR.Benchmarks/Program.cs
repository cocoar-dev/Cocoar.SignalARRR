using BenchmarkDotNet.Running;
using Cocoar.SignalARRR.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RoundtripBenchmarks).Assembly).Run(args);
