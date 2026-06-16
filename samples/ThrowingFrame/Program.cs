using Silk.NET.WebGPU;
using Skyline;
using Skyline.Input;
using Skyline.Render;

// What happens when your draw code throws.
//
// FrameLoop runs the per-frame ritual. If your OnRender throws, the loop tears
// the half-built frame down cleanly — it ends the pass, releases the encoder,
// cancels the acquired swapchain frame, and presents nothing — then lets the
// exception surface out of Run. A draw bug stays loud instead of being
// swallowed, and nothing leaks.
//
// The window renders a few calm frames, then throws on purpose. Pass
// --throw-on N to throw while drawing frame N (default 3). Escape quits early.

var throwOn = 3;
var argIdx = Array.IndexOf(args, "--throw-on");
if (argIdx >= 0 && argIdx + 1 < args.Length)
{
    _ = int.TryParse(args[argIdx + 1], out throwOn);
}

using var win = new AppWindow(new AppWindowOptions { Title = "Skyline - throwing frame", Width = 640, Height = 400 });
using var loop = FrameLoop.Attach(win, new FrameLoopOptions
{
    ClearColor = new Color { R = 0.10, G = 0.12, B = 0.16, A = 1.0 },
    Continuous = true,
});

var rendered = 0;

win.KeyInput += e =>
{
    if (e.IsDown && e.Key == Key.Escape)
    {
        win.RequestClose();
    }
};

loop.OnRender = (in Frame frame) =>
{
    rendered++;
    if (rendered >= throwOn)
    {
        throw new InvalidOperationException($"deliberate failure while drawing frame {rendered}");
    }
    // A normal frame: the loop's clear color is enough to show the window is alive.
};

win.Run(); // returns normally — a draw fault closes the window, it does not escape Run

if (loop.Outcome is Err fault)
{
    // The loop captured the draw exception, cancelled that frame, and closed the
    // window cleanly. Outcome hands it back, and PresentCount counts only the
    // frames that finished.
    Console.WriteLine($"THROWING FRAME OK: the render loop surfaced \"{fault.Error.Message}\"");
    Console.WriteLine($"  presented {loop.Surface.PresentCount} frames cleanly before the throw; the failed frame was cancelled, not presented");
    return 0;
}

Console.WriteLine("THROWING FRAME: the window closed without a draw error (unexpected)");
return 1;
