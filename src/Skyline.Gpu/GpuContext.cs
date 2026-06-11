using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace Skyline.Gpu;

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
    private WindowSurface? _surface;
    private bool _disposed;

    // The callbacks are native function pointers into these delegates. The
    // fields keep the delegates alive for the device's lifetime. Without
    // them the garbage collector may collect a delegate while wgpu still
    // holds its pointer.
    private readonly PfnErrorCallback _errorCallback;
    private readonly PfnDeviceLostCallback _lostCallback;

    private GpuContext(WebGPU wgpu, Instance* instance, Adapter* adapter)
    {
        _wgpu = wgpu;
        _instance = instance;
        _adapter = adapter;

        // The lost callback can only be registered through the device
        // descriptor, so the device request lives here where the callback
        // can close over this instance.
        _lostCallback = PfnDeviceLostCallback.From((reason, message, _) =>
            DeviceLost?.Invoke(reason, Marshal.PtrToStringUTF8((nint)message)));
        var desc = new DeviceDescriptor { DeviceLostCallback = _lostCallback };
        _device = RequestDevice(wgpu, adapter, in desc);

        _queue = _wgpu.DeviceGetQueue(_device);
        _wgpu.TryGetDeviceExtension(null, out _wgpuNative);

        _errorCallback = PfnErrorCallback.From((type, message, _) =>
            UncapturedError?.Invoke(type, Marshal.PtrToStringUTF8((nint)message)));
        _wgpu.DeviceSetUncapturedErrorCallback(_device, _errorCallback, null);
    }

    /// <summary>
    /// Build the full chain for a window: instance → surface → adapter
    /// (compatible with that surface) → device → queue. The resulting
    /// <see cref="Surface"/> is configured by the caller (see
    /// <see cref="WindowSurface.Configure"/>) — typically once at startup
    /// and again on every resize.
    /// </summary>
    public static GpuContext Create(INativeWindowSource window, GpuContextOptions? options = null,
        WindowSurfaceOptions? surfaceOptions = null)
    {
        options ??= new GpuContextOptions();
        var wgpu = WebGPU.GetApi();

        var instDesc = default(InstanceDescriptor);
        var instance = wgpu.CreateInstance(in instDesc);
        if (instance == null) throw new InvalidOperationException("wgpu CreateInstance failed");

        var surface = window.CreateWebGPUSurface(wgpu, instance);
        if (surface == null)
        {
            wgpu.InstanceRelease(instance);
            throw new InvalidOperationException("wgpu surface creation failed");
        }

        // Adapter and device requests resolve synchronously in wgpu-native,
        // so the chain reads straight-line.
        var adapter = RequestAdapter(wgpu, instance, surface, options);
        var context = new GpuContext(wgpu, instance, adapter);
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
        if (instance == null) throw new InvalidOperationException("wgpu CreateInstance failed");

        var adapter = RequestAdapter(wgpu, instance, null, options);
        return new GpuContext(wgpu, instance, adapter);
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
            if (status == RequestAdapterStatus.Success) adapter = a;
            else error = Marshal.PtrToStringUTF8((nint)message);
        });
        wgpu.InstanceRequestAdapter(instance, in opts, cb, null);
        if (adapter == null)
            throw new InvalidOperationException($"no wgpu adapter: {error ?? "unknown"}");
        return adapter;
    }

    private static Device* RequestDevice(WebGPU wgpu, Adapter* adapter, in DeviceDescriptor desc)
    {
        Device* device = null;
        string? error = null;
        var cb = PfnRequestDeviceCallback.From((status, d, message, _) =>
        {
            if (status == RequestDeviceStatus.Success) device = d;
            else error = Marshal.PtrToStringUTF8((nint)message);
        });
        wgpu.AdapterRequestDevice(adapter, in desc, cb, null);
        if (device == null)
            throw new InvalidOperationException($"no wgpu device: {error ?? "unknown"}");
        return device;
    }

    /// <summary>The window surface, when built via <see cref="Create"/>. Null for headless contexts.</summary>
    public WindowSurface? Surface => _surface;

    /// <summary>A validation or out-of-memory error wgpu could not attribute to an error scope.</summary>
    public event Action<ErrorType, string?>? UncapturedError;

    /// <summary>The device is gone (driver reset, destroyed). Resources on it are invalid.</summary>
    public event Action<DeviceLostReason, string?>? DeviceLost;

    /// <summary>
    /// Drive wgpu-native's device queue. <paramref name="wait"/> blocks until
    /// submitted work completes — what a buffer map waits on. Returns false
    /// when the wgpu-native extension is unavailable (a non-wgpu backend).
    /// </summary>
    public bool Poll(bool wait)
    {
        if (_wgpuNative is null) return false;
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
        if (_disposed) return;
        _disposed = true;
        _surface?.Dispose();
        if (_queue != null) _wgpu.QueueRelease(_queue);
        if (_device != null) _wgpu.DeviceRelease(_device);
        if (_adapter != null) _wgpu.AdapterRelease(_adapter);
        if (_instance != null) _wgpu.InstanceRelease(_instance);
    }

    internal void AttachSurface(WindowSurface surface) => _surface = surface;
}
