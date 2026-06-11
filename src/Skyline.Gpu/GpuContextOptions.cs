using Silk.NET.WebGPU;

namespace Skyline.Gpu;

/// <summary>Options for building a <see cref="GpuContext"/>.</summary>
public sealed class GpuContextOptions
{
    public PowerPreference PowerPreference { get; init; } = PowerPreference.HighPerformance;
}
