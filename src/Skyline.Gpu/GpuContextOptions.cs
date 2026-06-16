// SPDX-License-Identifier: Apache-2.0

using Silk.NET.WebGPU;

namespace Skyline.Gpu;

/// <summary>Options for building a <see cref="GpuContext"/>.</summary>
public sealed class GpuContextOptions
{
    public PowerPreference PowerPreference { get; init; } = PowerPreference.HighPerformance;

    /// <summary>
    /// When true and no subscriber is on <see cref="GpuContext.UncapturedError"/> or
    /// <see cref="GpuContext.DeviceLost"/>, Skyline.Gpu writes the error to stderr so
    /// it is never silently swallowed. Set to false when the host process handles all
    /// errors another way and wants no automatic output.
    /// </summary>
    public bool LogErrors { get; init; } = true;
}
