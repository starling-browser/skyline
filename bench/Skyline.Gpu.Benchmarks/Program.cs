using BenchmarkDotNet.Running;
using Skyline.Gpu.Benchmarks;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(FramePacerBenchmarks).Assembly).Run(args);

// The frame-rate cases answer "what FPS can this sustain": convert each
// mean frame time and report it against the standard display tiers.
int[] tiers = [240, 144, 120, 60];
foreach (var summary in summaries)
foreach (var report in summary.Reports)
{
    var method = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
    if (method is not (nameof(FrameRateBenchmarks.Frame) or nameof(FrameRateBenchmarks.OffscreenFrame)))
        continue;
    if (report.ResultStatistics is not { } stats)
        continue;
    var ms = stats.Mean / 1_000_000.0;
    var fps = 1000.0 / ms;
    var sustained = tiers.FirstOrDefault(t => fps >= t);
    Console.WriteLine(
        $"{report.BenchmarkCase.DisplayInfo}: {ms:0.000} ms/frame ≈ {fps:0} FPS — " +
        (sustained > 0 ? $"sustains {sustained} FPS" : "below 60 FPS"));
}
