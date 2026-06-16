// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Silk.NET.WebGPU;

namespace Skyline.Gpu;

/// <summary>
/// Throw helpers for failures only a broken native environment can
/// produce: no adapter, no device, surface creation refused, a backend
/// without polling, a failed buffer map. None can fire on a working
/// machine, and faking them would mean faking wgpu itself — so they are
/// excluded from coverage instead of padded with mock tests. Every call
/// site's check line still counts; only the throw bodies are excluded.
/// </summary>
[ExcludeFromCodeCoverage]
internal static unsafe class Guard
{
    [DoesNotReturn]
    internal static void FailInit(string what, string? error) =>
        throw new InvalidOperationException($"{what}: {error ?? "unknown"}");

    [DoesNotReturn]
    internal static void FailSurfaceCreation(WebGPU wgpu, Instance* instance)
    {
        wgpu.InstanceRelease(instance);
        throw new InvalidOperationException("wgpu surface creation failed");
    }

    [DoesNotReturn]
    internal static void FailPollRequired(string who) =>
        throw new InvalidOperationException($"{who} requires the wgpu-native poll extension");

    [DoesNotReturn]
    internal static void FailMap() =>
        throw new InvalidOperationException("readback buffer map failed");
}
