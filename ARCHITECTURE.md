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

Skyline targets WebGPU. The window host's native handle works with any
API that can build a swapchain, and a Vulkan, Metal, or OpenGL presenter
can plug in without Skyline.Gpu — supported as an escape hatch, not a
target. WebGPU is the path Skyline builds for, tests, and ships.

> **What's a swapchain?** The small set of textures the OS flips between
> to put your frames on screen. You draw into one while the last one is
> displayed. Vulkan, Metal, and Direct3D 12 can each build their own
> swapchain from the same window handle, so a presenter on any of them
> would also work here.

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
unsupported mode fails at `Configure`. Skyline.Gpu does not yet report
which modes a surface supports — a planned addition. Buffer count
(double versus triple) is not in WebGPU's API at all. wgpu picks it per
platform, so Skyline does not expose it. Frame pacing (how many frames
may be in flight) is future work.
