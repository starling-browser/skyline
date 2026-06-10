namespace Skyline;

/// <summary>Creation options for an <see cref="AppWindow"/>. Sizes are logical pixels.</summary>
public sealed class AppWindowOptions
{
    public string Title { get; init; } = "Skyline";
    public int Width { get; init; } = 800;
    public int Height { get; init; } = 600;
    public bool Resizable { get; init; } = true;
}
