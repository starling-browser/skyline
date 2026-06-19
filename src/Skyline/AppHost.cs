// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Skyline;

/// <summary>
/// Hosts many windows: one event loop, one render thread per window.
///
/// The split follows the platform rules. Window creation and event
/// processing must happen on the main thread (a macOS requirement GLFW
/// inherits), and one pump serves every window. Rendering has no such
/// rule — wgpu's device, queue, and surfaces are thread-safe — so each
/// window draws on its own thread, blocking on its own swapchain at its
/// own display's rate. Two vsynced windows on different monitors never
/// throttle each other, because no two blocking waits share a thread.
///
/// Threading contract for consumers: input callbacks and <see cref="Invoke"/>
/// actions run on the main thread. <see cref="AppWindowHandler.OnRenderFrame"/>
/// and <see cref="AppWindowHandler.OnResized"/> run on that window's render
/// thread, so all of a window's GPU work stays on one thread.
/// </summary>
public sealed class AppHost : IDisposable
{
    private sealed class Slot(AppWindow window)
    {
        public AppWindow Window { get; } = window;
        public Thread? Thread;
        public volatile bool Stop;
    }

    private readonly List<Slot> _slots = [];
    private readonly ConcurrentQueue<Action> _invokes = new();
    private bool _running;
    private bool _disposed;

    /// <summary>
    /// Fired on the main thread after a window's render thread has stopped,
    /// just before the host disposes the window. Release the window's GPU
    /// surfaces here, while the native window still exists.
    /// </summary>
    public event Action<AppWindow>? WindowClosed;

    /// <summary>
    /// Adopt a window. Main thread only. The host owns it from here:
    /// its render thread starts when <see cref="Run"/> does (or now, if
    /// already running), and the host disposes it when it closes.
    /// </summary>
    public void AddWindow(AppWindow window)
    {
        if (window.Host is not null)
        {
            throw new InvalidOperationException("window already belongs to a host");
        }

        window.Host = this;
        var slot = new Slot(window);
        _slots.Add(slot);
        if (_running)
        {
            StartRenderThread(slot);
        }
    }

    /// <summary>Queue work for the main thread and wake the loop. Callable from any thread.</summary>
    public void Invoke(Action action)
    {
        _invokes.Enqueue(action);
        Wake();
    }

    /// <summary>Wake the event loop from any thread.</summary>
    public static void Wake() => WindowBackendFactory.Pump.PostEmptyEvent();

    /// <summary>
    /// Run until every window has closed. Blocks the calling thread, which
    /// must be the main thread.
    /// </summary>
    public int Run()
    {
        _running = true;
        foreach (var slot in _slots.Where(s => s.Thread is null))
        {
            StartRenderThread(slot);
        }

        try
        {
            while (_slots.Count > 0)
            {
                // One global pump serves all windows. Poll plus a short
                // sleep, not WaitEventsTimeout: a blocking wait can't be
                // reliably interrupted during event bursts, so it would give
                // Invoke unbounded latency. Polling bounds it at the sleep
                // below.
                WindowBackendFactory.Pump.PollEvents();

                while (_invokes.TryDequeue(out var action))
                {
                    action();
                }

                for (var i = _slots.Count - 1; i >= 0; i--)
                {
                    if (_slots[i].Window.IsClosing)
                    {
                        Retire(_slots[i]);
                    }
                }

                Thread.Sleep(2);
            }
        }
        finally
        {
            // A throwing Invoke action must not leak render threads or
            // native windows: retire whatever is still open.
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                Retire(_slots[i]);
            }

            _running = false;
        }

        return 0;
    }

    private void StartRenderThread(Slot slot)
    {
        slot.Thread = new Thread(() => RenderLoop(slot))
        {
            IsBackground = true,
            Name = $"skyline-render: {slot.Window.Title}",
        };
        slot.Thread.Start();
    }

    private static void RenderLoop(Slot slot)
    {
        var window = slot.Window;
        var clock = Stopwatch.StartNew();
        var last = 0.0;
        while (!slot.Stop && !window.IsClosing)
        {
            // Apply resizes between frames, never during one: the consumer
            // reconfigures its swapchain inside Resized, which must not
            // race the frame it is drawing.
            if (window.TryConsumePendingResize(out var resized))
            {
                window.RaiseResized(resized);
            }

            if (window.IsMinimized)
            {
                // A minimized window's swapchain may stop returning
                // textures (macOS holds drawables for invisible windows),
                // so don't touch it until something changes.
                window.WaitForRedraw(50);
                continue;
            }

            if (window.ShouldRenderNow)
            {
                var now = clock.Elapsed.TotalSeconds;
                window.RaiseRenderFrame(now - last);
                last = now;
            }
            else
            {
                window.WaitForRedraw(8);
            }
        }
    }

    private void Retire(Slot slot)
    {
        slot.Stop = true;
        slot.Window.RequestRedraw(); // unblock a waiting render thread
        slot.Thread?.Join();
        _slots.Remove(slot);
        WindowClosed?.Invoke(slot.Window);
        slot.Window.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var i = _slots.Count - 1; i >= 0; i--)
        {
            Retire(_slots[i]);
        }
    }
}
