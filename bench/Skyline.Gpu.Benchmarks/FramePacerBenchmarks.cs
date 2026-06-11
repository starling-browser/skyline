using BenchmarkDotNet.Attributes;
using Silk.NET.WebGPU;

namespace Skyline.Gpu.Benchmarks;

/// <summary>
/// What does pacing cost per frame? All three benchmarks submit an empty
/// command buffer to a headless device — the floor for any frame.
///
/// Read the results as: EmptySubmit is the floor. EmptySubmitTracked adds
/// the pacer's bookkeeping (the work-done registration and counters) with
/// a cap so high it never blocks — the difference from the floor is the
/// pacer's true CPU overhead. EmptySubmitPaced uses the real cap of 2, so
/// its time is dominated by Wait blocking until the GPU finishes — that
/// is the backpressure doing its job, not overhead. MemoryDiagnoser
/// checks the zero-allocation claim on all three.
/// </summary>
[MemoryDiagnoser]
public unsafe class FramePacerBenchmarks
{
    private GpuContext _gpu = null!;
    private FramePacer _pacer = null!;
    private FramePacer _uncapped = null!;

    [GlobalSetup]
    public void Setup()
    {
        _gpu = GpuContext.CreateHeadless();
        _pacer = new FramePacer(_gpu, maxFramesInFlight: 2);
        _uncapped = new FramePacer(_gpu, maxFramesInFlight: int.MaxValue);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pacer.Dispose();
        _uncapped.Dispose();
        _gpu.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void EmptySubmit()
    {
        Submit();
        _gpu.Poll(wait: false); // keep wgpu's internal queue drained
    }

    [Benchmark]
    public void EmptySubmitTracked()
    {
        _uncapped.Wait(); // returns immediately — the cap is never reached
        Submit();
        _uncapped.FrameSubmitted();
        _gpu.Poll(wait: false);
    }

    [Benchmark]
    public void EmptySubmitPaced()
    {
        _pacer.Wait();
        Submit();
        _pacer.FrameSubmitted();
    }

    private void Submit()
    {
        var wgpu = _gpu.Api;
        var enc = wgpu.DeviceCreateCommandEncoder(_gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        var cmd = wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        wgpu.QueueSubmit(_gpu.QueueHandle, 1, &cmd);
        wgpu.CommandBufferRelease(cmd);
        wgpu.CommandEncoderRelease(enc);
    }
}
