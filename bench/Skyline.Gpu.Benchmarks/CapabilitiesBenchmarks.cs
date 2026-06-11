using BenchmarkDotNet.Attributes;
using Silk.NET.WebGPU;

namespace Skyline.Gpu.Benchmarks;

/// <summary>
/// The capability helpers run on hot paths only by accident, but they
/// still must not allocate. The instance mimics a real macOS surface:
/// Fifo and Immediate, no Mailbox.
/// </summary>
[MemoryDiagnoser]
public class CapabilitiesBenchmarks
{
    private readonly WindowSurfaceCapabilities _caps = new(
        [TextureFormat.Bgra8Unorm, TextureFormat.Bgra8UnormSrgb],
        [PresentMode.Fifo, PresentMode.Immediate],
        [CompositeAlphaMode.Opaque]);

    [Benchmark]
    public bool SupportsHit() => _caps.Supports(PresentMode.Immediate);

    [Benchmark]
    public bool SupportsMiss() => _caps.Supports(PresentMode.Mailbox);

    [Benchmark]
    public PresentMode Choose() => _caps.ChoosePresentMode(PresentMode.Mailbox, PresentMode.Immediate);
}
