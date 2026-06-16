using Skyline;
using Skyline.Input;
using Skyline.Render;
using Color = Silk.NET.WebGPU.Color;

namespace UnionApp;

public sealed class App
{
    private readonly AppWindowOptions _windowOptions;
    private Color _clear = new() { A = 1 };
    private FrameCallback? _onRender;
    private int _maxFrames;

    private App(AppWindowOptions windowOptions) => _windowOptions = windowOptions;

    public static App Window(AppWindowOptions options) => new(options);

    public App Clear(double r, double g, double b)
    {
        _clear = new Color { R = r, G = g, B = b, A = 1 };
        return this;
    }

    public App OnRender(FrameCallback onRender)
    {
        _onRender = onRender;
        return this;
    }

    /// <summary>Auto-close after this many presented frames. 0 runs until the window closes.</summary>
    public App ForFrames(int frames)
    {
        _maxFrames = frames;
        return this;
    }

    public FrameOutcome Run()
    {
        using var win = new AppWindow(_windowOptions);
        using var loop = FrameLoop.Attach(win, new FrameLoopOptions
        {
            ClearColor = _clear,
            Continuous = true,
        });

        var presented = 0;
        win.KeyInput += e =>
        {
            if (e.IsDown && e.Key == Key.Escape)
            {
                win.RequestClose();
            }
        };
        loop.OnRender = (in Frame frame) =>
        {
            _onRender?.Invoke(frame);
            presented++;
            if (_maxFrames > 0 && presented >= _maxFrames)
            {
                win.RequestClose();
            }
        };

        win.Run();

        // FrameLoop catches a draw fault, tears the half-built frame down, and
        // closes the window cleanly — it never escapes Run. Its Outcome is Err
        // with that exception, or Ok with the count that actually presented.
        return loop.Outcome;
    }
}
