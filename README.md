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
- **Windows that don't wait on each other.** `AppHost` runs many
  windows — one event loop, one render thread each. A window on a 60 Hz
  monitor never throttles one on a 144 Hz monitor.
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

Two windows on one device, each with its own render thread:

```sh
dotnet run --project samples/TwoWindows
```

A textured quad with an app-owned shader pipeline, vertex buffer,
texture, sampler, and bind group:

```sh
dotnet run --project samples/TexturedQuad
```

An interactive canvas that turns pointer input into dynamic GPU geometry:

```sh
dotnet run --project samples/InteractiveCanvas
```

## Tests

Both libraries are at 100% line coverage, from three vehicles:

- `tests/Skyline.Tests` and `tests/Skyline.Gpu.Tests` — MSTest, headless.
  The GPU tests run against a real device with no window.
- `tests/Skyline.WindowedTests` — a console harness for everything that
  needs a real window. GLFW requires the main thread on macOS, and test
  runners execute on worker threads, so these checks run as a plain
  program with an exit code.

Run everything and get the merged report:

```sh
./tools/cover.sh
```

The only excluded lines are native-failure guards (no GPU adapter, no
device, surface creation refused) collected in `Skyline.Gpu/Guard.cs` —
they cannot fire on a working machine, and the exclusion is documented
there.

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
are not yet exercised. Planned next: key modifier state and pointer
enter/leave.

## License

Apache-2.0. See [LICENSE](LICENSE).
