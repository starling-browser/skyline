using Silk.NET.WebGPU;

namespace Skyline.Gpu;

/// <summary>
/// Options for building a <see cref="GpuContext"/>. Skyline.Gpu uses
/// WebGPU's own vocabulary (Silk.NET enums) rather than mirroring it —
/// one less translation layer between you and the spec.
/// </summary>
public sealed class GpuContextOptions
{
    public PowerPreference PowerPreference { get; init; } = PowerPreference.HighPerformance;
}
