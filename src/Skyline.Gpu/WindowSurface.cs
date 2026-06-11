using Silk.NET.WebGPU;

namespace Skyline.Gpu;

/// <summary>
/// A window's swapchain: configure, acquire the current texture, present.
/// The wrapper's value is the failure handling. A resize or display change
/// invalidates the swapchain between frames, and <see cref="TryAcquireFrame"/>
/// absorbs that by reconfiguring and skipping the frame instead of handing
/// out a dead texture.
/// </summary>
public sealed unsafe class WindowSurface : IDisposable
{
    private readonly GpuContext _context;
    private readonly Surface* _surface;
    private readonly WindowSurfaceOptions _options;
    private Texture* _texture;
    private TextureView* _view;
    private bool _disposed;

    internal WindowSurface(GpuContext context, Surface* surface, WindowSurfaceOptions options)
    {
        _context = context;
        _surface = surface;
        _options = options;
    }

    /// <summary>Size set by the last <see cref="Configure"/>, in pixels.</summary>
    public (int Width, int Height) PixelSize { get; private set; }

    public TextureFormat Format => _options.Format;

    /// <summary>
    /// (Re)build the swapchain at this pixel size. Call once after creation
    /// and again on every framebuffer resize. Configuring discards surface
    /// contents — redraw after.
    /// </summary>
    public void Configure(int pixelWidth, int pixelHeight)
    {
        var w = Math.Max(1, pixelWidth);
        var h = Math.Max(1, pixelHeight);
        var config = new SurfaceConfiguration
        {
            Device = _context.DeviceHandle,
            Format = _options.Format,
            Usage = TextureUsage.RenderAttachment | _options.ExtraUsage,
            Width = (uint)w,
            Height = (uint)h,
            PresentMode = _options.PresentMode,
            AlphaMode = _options.AlphaMode,
        };
        _context.Api.SurfaceConfigure(_surface, in config);
        PixelSize = (w, h);
    }

    /// <summary>
    /// Acquire this frame's texture. On success, <see cref="CurrentTexture"/>
    /// and <see cref="CurrentView"/> are valid until <see cref="Present"/> or
    /// <see cref="CancelFrame"/>. Returns false when the swapchain is stale
    /// (resized, occluded, timed out). The surface reconfigures itself, so
    /// retry next frame. Throws when the device is gone.
    /// </summary>
    public bool TryAcquireFrame()
    {
        if (_texture != null)
            throw new InvalidOperationException("frame already acquired. Call Present or CancelFrame first.");

        SurfaceTexture st = default;
        _context.Api.SurfaceGetCurrentTexture(_surface, ref st);
        switch (st.Status)
        {
            case SurfaceGetCurrentTextureStatus.Success:
                break;
            case SurfaceGetCurrentTextureStatus.Timeout:
            case SurfaceGetCurrentTextureStatus.Outdated:
            case SurfaceGetCurrentTextureStatus.Lost:
                if (st.Texture != null) _context.Api.TextureRelease(st.Texture);
                if (PixelSize is { Width: > 0, Height: > 0 })
                    Configure(PixelSize.Width, PixelSize.Height);
                return false;
            default:
                throw new InvalidOperationException($"surface acquire failed: {st.Status}");
        }

        _texture = st.Texture;
        _view = _context.Api.TextureCreateView(_texture, (TextureViewDescriptor*)null);
        return true;
    }

    /// <summary>The acquired swapchain texture. Valid only between a successful <see cref="TryAcquireFrame"/> and <see cref="Present"/>.</summary>
    public Texture* CurrentTexture => _texture;

    /// <summary>A full view of <see cref="CurrentTexture"/>, ready as a render-pass color attachment.</summary>
    public TextureView* CurrentView => _view;

    /// <summary>Present the acquired frame and release it. Submit your command buffers first.</summary>
    public void Present()
    {
        if (_texture == null)
            throw new InvalidOperationException("no acquired frame to present");
        _context.Api.SurfacePresent(_surface);
        ReleaseFrame();
    }

    /// <summary>Release the acquired frame without presenting (the frame's work was abandoned).</summary>
    public void CancelFrame() => ReleaseFrame();

    private void ReleaseFrame()
    {
        if (_view != null) _context.Api.TextureViewRelease(_view);
        if (_texture != null) _context.Api.TextureRelease(_texture);
        _view = null;
        _texture = null;
    }

    /// <summary>Raw wgpu surface — the escape hatch.</summary>
    public Surface* SurfaceHandle => _surface;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseFrame();
        _context.Api.SurfaceRelease(_surface);
    }
}
