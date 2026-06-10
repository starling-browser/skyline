# Skyline

A native window with OS chrome, an event loop, and input — and no rendering
opinion. Skyline hands you the window's native surface handle and gets out
of the way. Bring your own presenter: WebGPU, Vulkan, Metal, OpenGL,
anything that can build a swapchain from a native window.

```csharp
using var win = new AppWindow(new() { Title = "hello" });
var renderer = MyRenderer.CreateSurface(win.Surface);   // your code, your API

win.Resized     += f => renderer.Configure(f.PixelWidth, f.PixelHeight);
win.RenderFrame += f => renderer.DrawAndPresent();
return win.Run();
```

## What Skyline owns

- Window creation with OS chrome (GLFW via Silk.NET) and DPI tracking.
- The event loop, with dirty-frame pacing (`IsDirty`). Idle apps sleep
  instead of free-running a core.
- Input as plain structs: pointer, key, and text events. No Silk.NET
  types leak into your code.
- Clipboard text.

What Skyline never does: touch a pixel. The render callback gives you
frame geometry and timing. Presenting is yours, through
`AppWindow.Surface` (Silk.NET's `INativeWindowSource`).

## Requirements

- .NET 10 SDK or later.
- A GPU. The sample uses wgpu (the native WebGPU implementation) and
  ships its binary via the `Silk.NET.WebGPU.Native.WGPU` package.

## Layout

- `src/Skyline` — the library. Depends only on Silk.NET windowing,
  input, and GLFW. No graphics API dependency.
- `samples/HelloWindow` — the proof that the seam works.

## Sample: HelloWindow

A Skyline window plus a raw wgpu clear-color renderer, written entirely
in the sample. An on-screen panel shows the controls and the live color
values.

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

Early. The window host and the sample are real and tested on macOS.
Windows and Linux paths exist through GLFW but are not yet exercised.
Planned next: an optional wgpu adapter package that wraps the sample's
device and swapchain boilerplate.

## License

Apache-2.0. See [LICENSE](LICENSE).
