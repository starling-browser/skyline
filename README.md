# Skyline

Ten lines from an empty `Main` to a live GPU frame.

Skyline is a WebGPU-first window and GPU library for .NET. It puts a
window on screen, hands you a working device and swapchain, and gets out
of the way. The rendering stays yours, in raw wgpu.

```csharp
using var win = new AppWindow(new() { Title = "hello" });
using var gpu = GpuContext.Create(win.Surface);

win.Resized     += f => gpu.Surface!.Configure(f.PixelWidth, f.PixelHeight);
win.RenderFrame += f =>
{
    if (!gpu.Surface!.TryAcquireFrame()) return;   // stale swapchain, retry next frame
    // encode passes against gpu.Surface.CurrentView with raw wgpu (gpu.Api)
    gpu.Surface.Present();
};
return win.Run();
```

## What you get

- **A window in one line.** OS chrome, event loop, DPI tracking, and
  clipboard. Input arrives as plain C# structs — pointer, key, text.
- **A GPU without the boilerplate.** The few hundred lines of unsafe
  setup every wgpu app starts with — instance, adapter, device, error
  callbacks that crash if you forget to root them — already written and
  tested.
- **Resizes that just work.** A resize or display change kills the
  swapchain mid-flight. Skyline catches it, rebuilds, and your next
  frame lands.
- **Tests that see pixels.** Read the presented frame back and assert
  on it. The sample's smoke test checks its own overlay this way.
- **Idle apps that idle.** Nothing changed, nothing renders. The loop
  sleeps instead of burning a core.
- **Latency that stays flat.** `FramePacer` caps frames in flight, so
  the CPU can't queue work the GPU hasn't earned. One native call per
  frame, zero allocation.
- **Nothing locked away.** Every raw wgpu handle is one property away.
  If WebGPU can do it, you still can.

## See it run

```sh
dotnet run --project samples/HelloWindow
```

A window, a clear color steered by your pointer, and a text overlay —
rendered through the full stack. Space toggles a hue cycle. Escape
quits.

Headless checks:

- `--frames N` auto-closes after N presented frames (smoke test).
- `--dump-hud` prints the text panel as ASCII art to check the built-in
  pixel font.
- `--verify-hud` reads the final frame back from the GPU and asserts the
  panel's pixels are in the presented image.

## Requirements

- .NET 10 SDK or later.
- A GPU. The sample uses wgpu (the native WebGPU implementation) and
  ships its binary via the `Silk.NET.WebGPU.Native.WGPU` package.

## Design

The rule behind the library: **mirror WebGPU, don't abstract it.**
Skyline wraps setup and present — the parts every app rewrites — and no
encoder, pipeline, or draw call. What each project owns, why this beats
a renderer abstraction, what Skyline adds over raw Silk.NET, and how
present modes and buffering work: [ARCHITECTURE.md](ARCHITECTURE.md).

## Status

Early. The window host, the WebGPU layer, and the sample are real and
tested on macOS. Windows and Linux paths exist through GLFW and wgpu but
are not yet exercised. Planned next: key modifier state, pointer
enter/leave, and a multi-window host.

## License

Apache-2.0. See [LICENSE](LICENSE).
