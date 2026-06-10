# Skyline

A native window with OS chrome, an event loop, and input — and no rendering
opinion. Skyline hands you the window's native surface handle and gets out of
the way. Bring your own presenter: wgpu, Vulkan, Metal, GL, anything that can
build a swapchain from a native window.

```csharp
using var win = new AppWindow(new() { Title = "hello" });
var renderer = MyRenderer.CreateSurface(win.Surface);   // your code, your API

win.Resized     += f => renderer.Configure(f.PixelWidth, f.PixelHeight);
win.RenderFrame += f => renderer.DrawAndPresent();
return win.Run();
```

What Skyline owns:

- Window creation with OS chrome (GLFW via Silk.NET), DPI tracking
- The event loop, with dirty-frame pacing (`IsDirty`) so idle apps sleep
  instead of free-running a core
- Input as plain structs: pointer, key, and text events
- Clipboard text

What Skyline never does: touch a pixel. The render callback gives you frame
geometry and timing; presenting is yours.

## Samples

- `samples/HelloWindow` — the proof. Skyline window + raw wgpu clear color.
  Move the pointer to steer the color, Space toggles a hue cycle, Escape
  quits. `--frames N` auto-closes after N presented frames (smoke test):

```sh
dotnet run --project samples/HelloWindow -- --frames 60
```

  Two extra headless checks: `--dump-hud` prints the HUD raster as ASCII
  art (font sanity), and `--verify-hud` reads the final frame back from
  the GPU and asserts the HUD panel pixels are in the presented image.
