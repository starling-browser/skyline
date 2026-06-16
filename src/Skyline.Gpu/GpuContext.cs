// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace Skyline.Gpu;

/// <summary>
/// Creates the wgpu surface for <see cref="GpuContext.Create(WebGPU, SurfaceFactory, GpuContextOptions?, WindowSurfaceOptions?)"/>.
/// Receives the instance the chain is being built on; returns the surface to
/// build the adapter against, or null when creation failed.
/// </summary>
public unsafe delegate Surface* SurfaceFactory(WebGPU wgpu, Instance* instance);

/// <summary>
/// The WebGPU init chain — instance, adapter, device, queue — plus device
/// error routing and polling. This class owns setup and lifetime. Every raw
/// handle stays reachable (<see cref="Api"/>, <see cref="DeviceHandle"/>, …)
/// so a consumer can drop to wgpu for anything the wrapper doesn't do.
/// </summary>
public sealed unsafe class GpuContext : IDisposable
{
    private readonly WebGPU _wgpu;
    private readonly Wgpu? _wgpuNative;
    private readonly Instance* _instance;
    private readonly Adapter* _adapter;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly GpuContextOptions _options;
    private WindowSurface? _surface;
    private bool _disposed;

    // The callbacks are native function pointers into these delegates. The
    // fields keep the delegates alive for the device's lifetime. Without
    // them the garbage collector may collect a delegate while wgpu still
    // holds its pointer.
    private readonly PfnErrorCallback _errorCallback;
    private readonly PfnDeviceLostCallback _lostCallback;

    private GpuContext(WebGPU wgpu, Instance* instance, Adapter* adapter, GpuContextOptions options)
    {
        _options = options;
        _wgpu = wgpu;
        _instance = instance;
        _adapter = adapter;

        // The lost callback can only be registered through the device
        // descriptor, so the device request lives here where the callback
        // can close over this instance.
        _lostCallback = PfnDeviceLostCallback.From((reason, message, _) =>
            RaiseDeviceLost(reason, Marshal.PtrToStringUTF8((nint)message)));
        var desc = new DeviceDescriptor { DeviceLostCallback = _lostCallback };
        _device = RequestDevice(wgpu, adapter, in desc);

        _queue = _wgpu.DeviceGetQueue(_device);
        _wgpu.TryGetDeviceExtension(null, out _wgpuNative);

        _errorCallback = PfnErrorCallback.From((type, message, _) =>
            RaiseUncapturedError(type, Marshal.PtrToStringUTF8((nint)message)));
        _wgpu.DeviceSetUncapturedErrorCallback(_device, _errorCallback, null);
    }

    /// <summary>
    /// Build the full chain for a window: instance → surface → adapter
    /// (compatible with that surface) → device → queue. The resulting
    /// <see cref="Surface"/> is configured by the caller — typically once
    /// at startup and again on every resize.
    /// </summary>
    public static GpuContext Create(INativeWindowSource window, GpuContextOptions? options = null,
        WindowSurfaceOptions? surfaceOptions = null)
    {
        return Create(WebGPU.GetApi(), window.CreateWebGPUSurface, options, surfaceOptions);
    }

    /// <summary>
    /// Same chain, with the platform pieces supplied by the caller: a
    /// <see cref="WebGPU"/> api object and a <see cref="SurfaceFactory"/>.
    /// The eject for platforms Silk.NET's window helper doesn't cover,
    /// without leaving the managed chain.
    /// </summary>
    public static GpuContext Create(WebGPU wgpu, SurfaceFactory createSurface, GpuContextOptions? options = null,
        WindowSurfaceOptions? surfaceOptions = null)
    {
        options ??= new GpuContextOptions();

        var instDesc = default(InstanceDescriptor);
        var instance = wgpu.CreateInstance(in instDesc);
        if (instance == null)
        {
            throw new InvalidOperationException("wgpu CreateInstance failed");
        }

        var surface = createSurface(wgpu, instance);
        if (surface == null)
        {
            Guard.FailSurfaceCreation(wgpu, instance);
        }

        // Adapter and device requests resolve synchronously in wgpu-native,
        // so the chain reads straight-line.
        var adapter = RequestAdapter(wgpu, instance, surface, options);
        var context = new GpuContext(wgpu, instance, adapter, options);
        context.AttachSurface(new WindowSurface(context, surface, surfaceOptions ?? new WindowSurfaceOptions()));
        return context;
    }

    /// <summary>
    /// Build the chain with no window: instance → adapter → device → queue.
    /// For compute, headless rendering, or tests. <see cref="Surface"/> is null.
    /// </summary>
    public static GpuContext CreateHeadless(GpuContextOptions? options = null)
    {
        options ??= new GpuContextOptions();
        var wgpu = WebGPU.GetApi();

        var instDesc = default(InstanceDescriptor);
        var instance = wgpu.CreateInstance(in instDesc);
        if (instance == null)
        {
            throw new InvalidOperationException("wgpu CreateInstance failed");
        }

        var adapter = RequestAdapter(wgpu, instance, null, options);
        return new GpuContext(wgpu, instance, adapter, options);
    }

    private static Adapter* RequestAdapter(WebGPU wgpu, Instance* instance, Surface* compatibleSurface, GpuContextOptions options)
    {
        Adapter* adapter = null;
        string? error = null;
        var opts = new RequestAdapterOptions
        {
            CompatibleSurface = compatibleSurface,
            PowerPreference = options.PowerPreference,
        };
        var cb = PfnRequestAdapterCallback.From((status, a, message, _) =>
        {
            // The message pointer is null on success, which marshals to null.
            if (status == RequestAdapterStatus.Success)
            {
                adapter = a;
            }

            error = Marshal.PtrToStringUTF8((nint)message);
        });
        wgpu.InstanceRequestAdapter(instance, in opts, cb, null);
        if (adapter == null)
        {
            Guard.FailInit("no wgpu adapter", error);
        }

        return adapter;
    }

    private static Device* RequestDevice(WebGPU wgpu, Adapter* adapter, in DeviceDescriptor desc)
    {
        Device* device = null;
        string? error = null;
        var cb = PfnRequestDeviceCallback.From((status, d, message, _) =>
        {
            if (status == RequestDeviceStatus.Success)
            {
                device = d;
            }

            error = Marshal.PtrToStringUTF8((nint)message);
        });
        wgpu.AdapterRequestDevice(adapter, in desc, cb, null);
        if (device == null)
        {
            Guard.FailInit("no wgpu device", error);
        }

        return device;
    }

    /// <summary>The window surface, when built via <see cref="Create"/>. Null for headless contexts.</summary>
    public WindowSurface? Surface => _surface;

    /// <summary>
    /// Build a surface for another window on this same device. One device
    /// serves any number of windows — multi-window apps should share one
    /// context rather than building a chain per window. The caller owns
    /// the returned surface and disposes it before this context.
    /// </summary>
    public WindowSurface CreateSurface(INativeWindowSource window, WindowSurfaceOptions? options = null) =>
        CreateSurface(window.CreateWebGPUSurface, options);

    /// <summary>
    /// Build a surface for another window on this device from a custom
    /// <see cref="SurfaceFactory"/> — the native-macOS / iOS eject, matching
    /// <see cref="Create(WebGPU, SurfaceFactory, GpuContextOptions?, WindowSurfaceOptions?)"/>.
    /// Lets a multi-window app share one device across windows on the native
    /// backend, where there is no <see cref="INativeWindowSource"/>.
    /// </summary>
    public WindowSurface CreateSurface(SurfaceFactory createSurface, WindowSurfaceOptions? options = null)
    {
        var surface = createSurface(_wgpu, _instance);
        if (surface == null)
        {
            throw new InvalidOperationException("wgpu surface creation failed");
        }

        return new WindowSurface(this, surface, options ?? new WindowSurfaceOptions());
    }

    /// <summary>
    /// Compile a WGSL shader from a managed string. The caller releases the
    /// returned pointer with <c>Api.ShaderModuleRelease</c> when done.
    /// Any compile error surfaces through <see cref="UncapturedError"/> after
    /// the next <see cref="Poll"/> call.
    /// </summary>
    public ShaderModule* CreateShaderModuleWgsl(string wgsl, string? label = null)
    {
        // A null array pins to a null pointer, which is the no-label case.
        fixed (byte* wgslPtr = NullTerminated(wgsl))
        fixed (byte* labelPtr = NullTerminated(label))
        {
            var wgslDesc = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct(null, SType.ShaderModuleWgslDescriptor),
                Code = wgslPtr,
            };
            var desc = new ShaderModuleDescriptor
            {
                NextInChain = &wgslDesc.Chain,
                Label = labelPtr,
            };
            return _wgpu.DeviceCreateShaderModule(_device, in desc);
        }
    }

    /// <summary>
    /// UTF-8 encodes <paramref name="value"/> with a trailing NUL so wgpu-native
    /// reads it as a C string, or returns null when <paramref name="value"/> is
    /// null — pinning a null array yields a null pointer.
    /// </summary>
    private static byte[]? NullTerminated(string? value)
    {
        if (value is null)
        {
            return null;
        }
        var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, bytes);
        return bytes;
    }

    /// <summary>
    /// Build a render pipeline from <see cref="PipelineOptions"/> — WebGPU's
    /// <see cref="RenderPipelineDescriptor"/> filled with overridable defaults.
    /// Returns a raw pipeline the caller releases with
    /// <c>Api.RenderPipelineRelease</c>. A compile or layout error surfaces
    /// through <see cref="UncapturedError"/> after the next <see cref="Poll"/>.
    /// </summary>
    public RenderPipeline* CreatePipeline(PipelineOptions options)
    {
        // Addressable stack copy so the color target can point at it when blending.
        var blend = options.Blend.GetValueOrDefault();
        fixed (byte* vsEntry = NullTerminated(options.VertexEntry))
        fixed (byte* fsEntry = NullTerminated(options.FragmentEntry))
        fixed (byte* labelPtr = NullTerminated(options.Label))
        fixed (VertexBufferLayout* buffers = options.VertexBuffers)
        {
            var colorTarget = new ColorTargetState
            {
                Format = options.ColorFormat,
                Blend = options.Blend.HasValue ? &blend : null,
                WriteMask = options.WriteMask,
            };
            var fragment = new FragmentState
            {
                Module = options.Shader,
                EntryPoint = fsEntry,
                TargetCount = 1,
                Targets = &colorTarget,
            };
            var desc = new RenderPipelineDescriptor
            {
                Layout = options.Layout,
                Vertex = new VertexState
                {
                    Module = options.Shader,
                    EntryPoint = vsEntry,
                    BufferCount = (uint)options.VertexBuffers.Length,
                    Buffers = buffers,
                },
                Primitive = new PrimitiveState
                {
                    Topology = options.Topology,
                    FrontFace = options.FrontFace,
                    CullMode = options.CullMode,
                },
                Multisample = new MultisampleState { Count = options.SampleCount, Mask = uint.MaxValue, AlphaToCoverageEnabled = false },
                Fragment = &fragment,
                Label = labelPtr,
            };
            return _wgpu.DeviceCreateRenderPipeline(_device, in desc);
        }
    }

    /// <summary>
    /// Build a bind group for <paramref name="layout"/> from raw entries —
    /// WebGPU's <see cref="BindGroupDescriptor"/> filled for you. Returns a
    /// raw bind group the caller releases with <c>Api.BindGroupRelease</c>.
    /// </summary>
    public BindGroup* CreateBindGroup(BindGroupLayout* layout, ReadOnlySpan<BindGroupEntry> entries, string? label = null)
    {
        fixed (BindGroupEntry* entryPtr = entries)
        fixed (byte* labelPtr = NullTerminated(label))
        {
            var desc = new BindGroupDescriptor
            {
                Layout = layout,
                EntryCount = (uint)entries.Length,
                Entries = entryPtr,
                Label = labelPtr,
            };
            return _wgpu.DeviceCreateBindGroup(_device, in desc);
        }
    }

    /// <summary>
    /// Create a 2D color texture to render into and then sample — an offscreen
    /// target for compositing. Usage defaults to
    /// <see cref="TextureUsage.RenderAttachment"/> | <see cref="TextureUsage.TextureBinding"/>
    /// (draw into it, sample it back) with no copy, so the pixels never leave
    /// the GPU. Add <paramref name="extraUsage"/> when needed — for example
    /// <see cref="TextureUsage.CopySrc"/> to read it back. One mip level, one
    /// sample. Returns a raw texture the caller releases with
    /// <c>Api.TextureRelease</c>; build a view with <c>Api.TextureCreateView</c>.
    /// </summary>
    public Texture* CreateColorTarget(int width, int height, TextureFormat format,
        TextureUsage extraUsage = TextureUsage.None, string? label = null)
    {
        fixed (byte* labelPtr = NullTerminated(label))
        {
            var desc = new TextureDescriptor
            {
                Dimension = TextureDimension.Dimension2D,
                Format = format,
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                MipLevelCount = 1,
                SampleCount = 1,
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | extraUsage,
                Label = labelPtr,
            };
            return _wgpu.DeviceCreateTexture(_device, in desc);
        }
    }

    /// <summary>A validation or out-of-memory error wgpu could not attribute to an error scope.</summary>
    public event Action<ErrorType, string?>? UncapturedError;

    /// <summary>The device is gone (driver reset, destroyed). Resources on it are invalid.</summary>
    public event Action<DeviceLostReason, string?>? DeviceLost;

    /// <summary>
    /// Routes an uncaptured error to subscribers or, when there are none and
    /// <see cref="GpuContextOptions.LogErrors"/> is true, writes it to stderr.
    /// </summary>
    internal void RaiseUncapturedError(ErrorType type, string? message)
    {
        if (UncapturedError is { } handler)
        {
            handler(type, message);
            return;
        }
        if (_options.LogErrors)
        {
            Console.Error.WriteLine($"wgpu error ({type}): {message}");
        }
    }

    /// <summary>
    /// Routes a device-lost event to subscribers or, when there are none and
    /// <see cref="GpuContextOptions.LogErrors"/> is true, writes it to stderr.
    /// <see cref="DeviceLostReason.Destroyed"/> is a clean teardown and is
    /// never logged.
    /// </summary>
    internal void RaiseDeviceLost(DeviceLostReason reason, string? message)
    {
        if (DeviceLost is { } handler)
        {
            handler(reason, message);
            return;
        }
        if (reason == DeviceLostReason.Destroyed)
        {
            return;
        }

        if (_options.LogErrors)
        {
            Console.Error.WriteLine($"wgpu device lost ({reason}): {message}");
        }
    }

    /// <summary>True when the wgpu-native poll extension is available. <see cref="TextureReadback"/> and <see cref="FramePacer"/> require it.</summary>
    public bool SupportsPoll => _wgpuNative is not null;

    /// <summary>
    /// Drive wgpu-native's device queue. <paramref name="wait"/> blocks until
    /// submitted work completes — what a buffer map waits on. Returns false
    /// when the wgpu-native extension is unavailable (a non-wgpu backend).
    /// </summary>
    public bool Poll(bool wait)
    {
        if (_wgpuNative is null)
        {
            return false;
        }

        _wgpuNative.DevicePoll(_device, wait, null);
        return true;
    }

    /// <summary>Raw Silk.NET WebGPU API — the escape hatch for everything Skyline.Gpu doesn't wrap.</summary>
    public WebGPU Api => _wgpu;

    public Instance* InstanceHandle => _instance;
    public Adapter* AdapterHandle => _adapter;
    public Device* DeviceHandle => _device;
    public Queue* QueueHandle => _queue;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _surface?.Dispose();
        if (_queue != null)
        {
            _wgpu.QueueRelease(_queue);
        }

        if (_device != null)
        {
            _wgpu.DeviceRelease(_device);
        }

        if (_adapter != null)
        {
            _wgpu.AdapterRelease(_adapter);
        }

        if (_instance != null)
        {
            _wgpu.InstanceRelease(_instance);
        }
        // After the device is gone, wgpu holds no callback pointers.
        _errorCallback.Dispose();
        _lostCallback.Dispose();
    }

    internal void AttachSurface(WindowSurface surface) => _surface = surface;
}
