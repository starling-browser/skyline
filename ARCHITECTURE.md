# Skyline architecture

How Skyline is put together, what each project owns, and the rules that
keep it small.

## The layer stack

```
┌─────────────────────────────────────────┐
│            your application             │
│       (pipelines, passes, draws)        │
├─────────────────────┬───────────────────┤
│  Skyline            │  Skyline.Gpu      │
│  window, loop,      │  device, swap-    │
│  input, clipboard   │  chain, present   │
├─────────────────────┴───────────────────┤
│       Silk.NET  (GLFW · WebGPU)         │
├─────────────────────────────────────────┤
│  wgpu-native → Metal / Vulkan / DX12    │
└─────────────────────────────────────────┘
```

Two projects:

- `src/Skyline` — the window host. Depends only on Silk.NET windowing,
  input, and GLFW. No graphics API dependency.
- `src/Skyline.Gpu` — the WebGPU layer. Depends on Silk.NET.WebGPU. It
  does not reference the window host — it works against any
  `INativeWindowSource`. It also doesn't pin a native WebGPU build:
  apps reference `Silk.NET.WebGPU.Native.WGPU` (or another
  implementation) themselves.

There is also `bench/Skyline.Gpu.Benchmarks` — BenchmarkDotNet checks on
the performance claims below. Run it with
`dotnet run -c Release --project bench/Skyline.Gpu.Benchmarks`.

Skyline targets WebGPU. The window host's native handle works with any
API that can build a swapchain, and a Vulkan, Metal, or OpenGL presenter
can plug in without Skyline.Gpu — supported as an escape hatch, not a
target. WebGPU is the path Skyline builds for, tests, and ships.

> **What's a swapchain?** The small set of textures the OS flips between
> to put your frames on screen. You draw into one while the last one is
> displayed. Vulkan, Metal, and Direct3D 12 can each build their own
> swapchain from the same window handle, so a presenter on any of them
> would also work here.

## Multi-window: AppHost

`AppHost` hosts many windows with one event loop and one render thread
per window:

```
main thread                      render thread (per window)
───────────                      ──────────────────────────
WaitEventsTimeout ── pumps ──▶   apply pending resize
input callbacks fire             pace (FramePacer)
route close / Invoke()           acquire → encode → present
        ▲                                │
        └──── Wake() / RequestRedraw() ──┘
```

The split follows the platform rules. GLFW's event pump is global and
must run on the main thread (a macOS requirement), so one loop serves
every window. Rendering has no such rule — wgpu's device, queue, and
surfaces are thread-safe — so each window draws on its own thread and
blocks on its own swapchain at its own display's rate. That kills the
classic multi-monitor stall: two vsynced windows on a 60 Hz and a
144 Hz panel never share a blocking wait, so neither throttles the
other. One `GpuContext` serves all windows — extra windows get surfaces
from `CreateSurface`.

The threading contract: input events and `Invoke` actions run on the
main thread. `RenderFrame` and `Resized` run on that window's render
thread, so all of a window's GPU work stays on one thread. Resizes are
parked and applied between frames, never during one. Minimized windows
are skipped entirely — macOS stops returning drawables for invisible
windows, and touching their swapchain would hang the thread. Idle is
event-driven: the pump sleeps in `WaitEventsTimeout`, input wakes it
instantly, and `Wake()`/`RequestRedraw()` wake it from any thread.

## What the window host owns

- Window creation with OS chrome (GLFW via Silk.NET) and DPI tracking.
- The event loop, with dirty-frame pacing (`IsDirty`). Idle apps sleep
  instead of free-running a core.
- Input as plain structs: pointer, key, and text events. No Silk.NET
  types leak into your code.
- Clipboard text.

The window host never touches a pixel. The render callback gives you
frame geometry and timing. The window's native handle stays available
through `AppWindow.Surface` (Silk.NET's `INativeWindowSource`).

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

One frame looks like this:

```
RenderFrame event (Skyline)
        │
        ▼
TryAcquireFrame ──false──▶ surface reconfigures, frame skipped,
        │ true             retried next frame
        ▼
encode passes against CurrentView      ◀── your code, raw wgpu
        │
        ▼
submit, then Present (Skyline.Gpu)
```

## The design rule: mirror WebGPU, don't abstract it

WebGPU is already the portability layer — one spec, many
implementations. A second abstraction on top would add a vocabulary to
learn and hide capability, without adding any portability. So
Skyline.Gpu uses WebGPU's own vocabulary (Silk.NET types in options, raw
handles via `GpuContext.Api`, `DeviceHandle`,
`WindowSurface.CurrentView`, …) and wraps no encoder, pipeline, or draw
call. Everything past setup and present is your code against raw wgpu.
Additions that invent a renderer API on top of WebGPU belong in a
different library.

**Why not just Silk.NET?** Silk.NET is the binding — a one-to-one,
unsafe mapping of the C API. You get raw pointers, manual release calls,
and callbacks you must keep alive yourself. Skyline.Gpu is the few
hundred lines every app writes on top of that binding, with the sharp
edges already hit: an init chain with real error messages, callback
delegates rooted so the garbage collector cannot collect them while wgpu
still holds their pointers, stale-swapchain recovery on resize,
row-aligned readback, and correct dispose ordering. The binding answers
how to call wgpu from C#. Skyline.Gpu answers how to get a device and
swapchain that survive resizes.

## Present modes and buffering

`WindowSurfaceOptions.PresentMode` passes WebGPU's four modes through:

- `Fifo` (default) — the vsync queue. Frames wait their turn. The only
  mode guaranteed everywhere.
- `FifoRelaxed` — vsync, but a frame that misses the beat shows right
  away (it may tear) instead of waiting a full interval.
- `Mailbox` — vsync with no queue: the newest finished frame replaces
  the one waiting. No tearing, low latency. Triple buffering in
  practice.
- `Immediate` — no vsync. Lowest latency, tearing likely.

Every mode except Fifo depends on the platform and driver, and an
unsupported mode fails at `Configure`. `WindowSurface.Capabilities`
reports what the surface supports — formats, present modes, alpha
modes — with one native query on first access, cached after.
`ChoosePresentMode` picks the first supported mode from your preference
list, falling back to Fifo:

```csharp
var surface = gpu.Surface!;
surface.PresentMode = surface.Capabilities.ChoosePresentMode(PresentMode.Mailbox);
surface.Configure(w, h);
```

Buffer count (double versus triple) is not in WebGPU's API at all. wgpu
picks it per platform, so Skyline does not expose it.

## Frame pacing

A fast CPU records frames quicker than the GPU draws them. Unchecked,
frames pile up in the queue and every input waits behind the pile —
throughput looks fine while latency grows. `FramePacer` caps how many
frames may be submitted but not yet finished:

```csharp
var pacer = new FramePacer(gpu, maxFramesInFlight: 2);

// each frame:
pacer.Wait();                  // block until a slot frees
// acquire, encode, QueueSubmit
pacer.FrameSubmitted();        // count the submit, register completion
```

The default of 2 lets the CPU record one frame while the GPU draws
another. `TryWait` is the non-blocking variant for loops that would
rather skip than stall.

The pacer is built for per-frame use. The completion callback is created
once and reused — never allocated per frame. Steady-state cost per frame
is one native call and two interlocked operations. The callback frees
its slot on every completion status, including device loss, so `Wait`
cannot hang on a dead device. Like `TextureReadback`, it needs the
wgpu-native poll extension, and the constructor fails fast without it.

Measured (BenchmarkDotNet, M-series Mac, empty submits to a headless
device): the bookkeeping adds about 60 nanoseconds to a 3.5 microsecond
submit, with zero managed allocation. With the cap engaged the loop sits
in `Wait` for the GPU's actual completion time — that is the
backpressure working, not overhead.

## Sustainable frame rate

The frame-rate benchmarks run a real paced frame loop at 2560x1440 and
answer "what FPS can this stack sustain":

- **Offscreen** (render and submit, no present): 0.7–0.9 ms per frame,
  about 1,200–1,400 FPS — the stack sustains a 240 Hz panel with five
  times headroom, allocation-free.
- **Windowed**: 8.3 ms per frame, exactly the test panel's 120 Hz. Even
  in Immediate mode, macOS hands out drawables at the display's refresh
  rate. The wall is the display pipeline, not Skyline — the same loop
  with the display out of the way runs ten times faster.
