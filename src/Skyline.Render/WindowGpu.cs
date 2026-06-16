// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics.CodeAnalysis;
using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Render;

/// <summary>
/// Builds GPU resources for an <see cref="AppWindow"/> on either backend: a GLFW
/// window through its Silk.NET surface source, a native macOS window through its
/// <c>CAMetalLayer</c> and wgpu's surface factory — the same eject an iOS app
/// uses. This is the surface path that works on every backend, so reach for it
/// instead of <c>GpuContext.Create(window.Surface)</c>, which throws on the
/// native macOS backend.
///
/// Dispatch runs on the window's <see cref="WindowSurfaceSource"/> union, the
/// one seam that names the two backends, so a new backend adds one case here
/// rather than another null-check at every call site. The Metal-layer branch
/// needs a real <c>CAMetalLayer</c> the portable coverage gate never deploys,
/// and faking the pointer would crash wgpu, so this is excluded from coverage
/// like <c>Guard.cs</c>; the windowed harness exercises the GLFW branch
/// end-to-end through <see cref="FrameLoop.Attach"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public static unsafe class WindowGpu
{
    /// <summary>
    /// Build the whole chain — instance, surface, adapter, device, queue — for
    /// <paramref name="window"/> on whichever backend it uses. The single-window
    /// and first-window-of-many entry point.
    /// </summary>
    public static GpuContext CreateContext(AppWindow window, GpuContextOptions? gpu = null, WindowSurfaceOptions? surface = null) =>
        window.BackendSurfaceSource switch
        {
            WindowSurfaceSource.MetalLayer m => GpuContext.Create(WebGPU.GetApi(), MetalSurface(m.Layer), gpu, surface),
            WindowSurfaceSource.Native n => GpuContext.Create(n.Source, gpu, surface),
            _ => throw new InvalidOperationException("unknown window surface source"),
        };

    /// <summary>
    /// Build an additional surface for <paramref name="window"/> on an existing
    /// <paramref name="context"/> — the multi-window path, where one device
    /// serves every window. Works on both backends.
    /// </summary>
    public static WindowSurface CreateSurface(GpuContext context, AppWindow window, WindowSurfaceOptions? options = null) =>
        window.BackendSurfaceSource switch
        {
            WindowSurfaceSource.MetalLayer m => context.CreateSurface(MetalSurface(m.Layer), options),
            WindowSurfaceSource.Native n => context.CreateSurface(n.Source, options),
            _ => throw new InvalidOperationException("unknown window surface source"),
        };

    private static SurfaceFactory MetalSurface(nint layer) => (api, instance) =>
    {
        var metal = new SurfaceDescriptorFromMetalLayer
        {
            Chain = new ChainedStruct(null, SType.SurfaceDescriptorFromMetalLayer),
            Layer = (void*)layer,
        };
        var desc = new SurfaceDescriptor { NextInChain = &metal.Chain };
        return api.InstanceCreateSurface(instance, in desc);
    };
}
