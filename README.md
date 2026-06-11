# Skyline

A native window with OS chrome, an event loop, and input — plus an optional
WebGPU layer that owns the device chain, swapchain, and present mechanics.
The core library has no rendering opinion. Skyline.Gpu has exactly one:
WebGPU, mirrored rather than abstracted.

```csharp
using var win = new AppWindow(new() { Title = "hello" });
using var gpu = GpuContext.Create(win.Surface);

win.Resized     += f => gpu.Surface!.Configure(f.PixelWidth, f.PixelHeight);
win.RenderFrame += f =>
{
    if (!gpu.Surface!.TryAcquireFrame()) return;   // stale swapchain; retry next frame
    // encode passes against gpu.Surface.CurrentView with raw wgpu (gpu.Api)
    gpu.Surface.Present();
};
return win.Run();
```

## What Skyline owns

- Window creation with OS chrome (GLFW via Silk.NET) and DPI tracking.
- The event loop, with dirty-frame pacing (`IsDirty`). Idle apps sleep
  instead of free-running a core.
- Input as plain structs: pointer, key, and text events. No Silk.NET
  types leak into your code.
- Clipboard text.

The core library never touches a pixel. The render callback gives you frame
geometry and timing. The window's native handle stays available through
`AppWindow.Surface` (Silk.NET's `INativeWindowSource`), so any presenter —
Vulkan, Metal, OpenGL — can plug in without Skyline.Gpu.

## What Skyline.Gpu owns

The parts of WebGPU every app rewrites, and nothing more:

- The init chain: instance → surface → adapter → device → queue
  (`GpuContext`), with device-lost and uncaptured-error events.
- The swapchain (`WindowSurface`): configure, acquire, present. A stale
  swapchain (resize, display change) reconfigures itself and skips the
  frame instead of handing out a dead texture.
- GPU-to-processor readback for screenshots and pixel tests
  (`TextureReadback`), encoded into your own submission so what you read
  is exactly what presented.

The design rule: **mirror WebGPU, don't abstract it.** Skyline.Gpu uses
WebGPU's own vocabulary (Silk.NET types in options, raw handles via
`GpuContext.Api`, `DeviceHandle`, `WindowSurface.CurrentView`, …) and wraps
no encoder, pipeline, or draw call. Everything past setup and present is
your code against raw wgpu. Additions that invent a renderer API on top of
WebGPU belong in a different library.

Skyline.Gpu does not reference the core library — it works against any
`INativeWindowSource`. It also doesn't pin a native WebGPU build: apps
reference `Silk.NET.WebGPU.Native.WGPU` (or another implementation)
themselves.

## Requirements

- .NET 10 SDK or later.
- A GPU. The sample uses wgpu (the native WebGPU implementation) and
  ships its binary via the `Silk.NET.WebGPU.Native.WGPU` package.

## Layout

- `src/Skyline` — the window host. Depends only on Silk.NET windowing,
  input, and GLFW. No graphics API dependency.
- `src/Skyline.Gpu` — the WebGPU layer. Depends on Silk.NET.WebGPU.
- `samples/HelloWindow` — the proof that both seams work.

## Sample: HelloWindow

A Skyline window plus a clear-color renderer: Skyline.Gpu does setup and
present, the sample encodes its clear pass and HUD copy with raw wgpu
through the escape hatches. An on-screen panel shows the controls and the
live color values.

- Move the pointer to steer the color.
- Space toggles a slow hue cycle.
- Escape quits.

```sh
dotnet run --project samples/HelloWindow
```

Headless checks:

- `--frames N` auto-closes after N presented frames (smoke test).
- `--dump-hud` prints the text panel as ASCII art to check the built-in
  pixel font.
- `--verify-hud` reads the final frame back from the GPU and asserts the
  panel's pixels are in the presented image.

## Status

Early. The window host, the WebGPU layer, and the sample are real and
tested on macOS. Windows and Linux paths exist through GLFW and wgpu but
are not yet exercised. Planned next: key modifier state, pointer
enter/leave, and a multi-window host.

## License

Apache-2.0. See [LICENSE](LICENSE).
