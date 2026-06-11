using Silk.NET.WebGPU;

namespace Skyline.Gpu;

/// <summary>
/// What this surface supports on this adapter: pixel formats, present
/// modes, and alpha modes. Fetched once and cached — capabilities do not
/// change for a given surface and adapter pair.
/// </summary>
public sealed class WindowSurfaceCapabilities
{
    private readonly TextureFormat[] _formats;
    private readonly PresentMode[] _presentModes;
    private readonly CompositeAlphaMode[] _alphaModes;

    internal WindowSurfaceCapabilities(TextureFormat[] formats, PresentMode[] presentModes, CompositeAlphaMode[] alphaModes)
    {
        _formats = formats;
        _presentModes = presentModes;
        _alphaModes = alphaModes;
    }

    /// <summary>Supported pixel formats, preferred first.</summary>
    public ReadOnlySpan<TextureFormat> Formats => _formats;

    public ReadOnlySpan<PresentMode> PresentModes => _presentModes;

    public ReadOnlySpan<CompositeAlphaMode> AlphaModes => _alphaModes;

    public bool Supports(PresentMode mode)
    {
        // At most four entries — a scan beats any lookup structure.
        foreach (var m in _presentModes)
            if (m == mode) return true;
        return false;
    }

    /// <summary>
    /// The first of <paramref name="preferred"/> this surface supports,
    /// or Fifo — the one mode WebGPU guarantees — when none match.
    /// </summary>
    public PresentMode ChoosePresentMode(params ReadOnlySpan<PresentMode> preferred)
    {
        foreach (var mode in preferred)
            if (Supports(mode)) return mode;
        return PresentMode.Fifo;
    }
}
