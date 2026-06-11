using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Skyline.Gpu;

/// <summary>
/// Read a texture's pixels back to the processor — for screenshots and
/// pixel-asserting tests, not per-frame work. The copy must be encoded into
/// the same command submission that produced the texture. A copy submitted
/// later is allowed to miss work that was still queued. So the flow is
/// split: <see cref="Encode"/> inside your encoder, submit, then
/// <see cref="Resolve"/>.
/// </summary>
public sealed unsafe class TextureReadback : IDisposable
{
    private readonly GpuContext _context;
    private readonly Buffer* _buffer;
    private readonly int _width;
    private readonly int _height;
    private readonly int _rowPitch;
    private bool _disposed;

    public TextureReadback(GpuContext context, int pixelWidth, int pixelHeight)
    {
        _context = context;
        _width = pixelWidth;
        _height = pixelHeight;
        // wgpu requires 256-byte row alignment on texture-to-buffer copies.
        _rowPitch = (pixelWidth * 4 + 255) & ~255;
        var desc = new BufferDescriptor
        {
            Size = (ulong)(_rowPitch * pixelHeight),
            Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
        };
        _buffer = context.Api.DeviceCreateBuffer(context.DeviceHandle, in desc);
    }

    /// <summary>Encode the texture→buffer copy. Call inside the encoder whose submission produces the texture's final contents.</summary>
    public void Encode(CommandEncoder* encoder, Texture* texture)
    {
        var src = new ImageCopyTexture { Texture = texture, MipLevel = 0, Origin = default, Aspect = TextureAspect.All };
        var dst = new ImageCopyBuffer
        {
            Buffer = _buffer,
            Layout = new TextureDataLayout { Offset = 0, BytesPerRow = (uint)_rowPitch, RowsPerImage = (uint)_height },
        };
        var extent = new Extent3D { Width = (uint)_width, Height = (uint)_height, DepthOrArrayLayers = 1 };
        _context.Api.CommandEncoderCopyTextureToBuffer(encoder, in src, in dst, in extent);
    }

    /// <summary>
    /// Map the buffer (blocking on <see cref="GpuContext.Poll"/>) and return
    /// the pixels, tightly packed at 4 bytes per pixel — alignment padding
    /// stripped. Call after the submission that included <see cref="Encode"/>.
    /// </summary>
    public byte[] Resolve()
    {
        var size = (nuint)(_rowPitch * _height);
        var mapped = false;
        var failed = false;
        var cb = PfnBufferMapCallback.From((status, _) =>
        {
            if (status == BufferMapAsyncStatus.Success) mapped = true;
            else failed = true;
        });
        _context.Api.BufferMapAsync(_buffer, MapMode.Read, 0, size, cb, null);

        while (!mapped && !failed)
        {
            if (!_context.Poll(wait: true))
                throw new InvalidOperationException("texture readback requires the wgpu-native poll extension");
        }
        if (failed)
            throw new InvalidOperationException("readback buffer map failed");

        var data = (byte*)_context.Api.BufferGetConstMappedRange(_buffer, 0, size);
        var pixels = new byte[_width * _height * 4];
        for (var y = 0; y < _height; y++)
        {
            var row = new ReadOnlySpan<byte>(data + y * _rowPitch, _width * 4);
            row.CopyTo(pixels.AsSpan(y * _width * 4));
        }
        _context.Api.BufferUnmap(_buffer);
        return pixels;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_buffer != null) _context.Api.BufferRelease(_buffer);
    }
}
