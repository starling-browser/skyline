using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Skyline.Gpu.Benchmarks.FramePacerBenchmarks).Assembly).Run(args);
