// SPDX-License-Identifier: Apache-2.0
using Silk.NET.WebGPU;
using Skyline;
using Skyline.Render;

// An opaque window with a unified title bar: the traffic lights float over the
// content and there is no separate top bar. Same merged-toolbar look as the
// transparent sample, but the window is solid. macOS-native; other backends
// fall back to a standard decorated window.
var window = new AppWindow(new AppWindowOptions
{
    Title = "Unified titlebar — Skyline",
    Width = 720,
    Height = 480,
    Chrome = ChromeMode.UnifiedTitlebar,
});

// --frames N closes after N presented frames, for a headless smoke test.
var maxFrames = ReadFrameCap(args);
var frames = 0;

// Opaque (alpha 1) so the window is solid. The content fills the whole window,
// running up under the floating buttons where a title bar would be.
using var loop = FrameLoop.Attach(window, new FrameLoopOptions
{
    Continuous = true,
    ClearColor = new Color { R = 0.12, G = 0.14, B = 0.20, A = 1.0 },
});

loop.OnRender = (in Frame _) =>
{
    if (maxFrames > 0 && ++frames >= maxFrames)
    {
        window.RequestClose();
    }
};

return window.Run();

static int ReadFrameCap(string[] args)
{
    var i = Array.IndexOf(args, "--frames");
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) ? n : 0;
}
