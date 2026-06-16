// SPDX-License-Identifier: Apache-2.0

using Silk.NET.WebGPU;

namespace Skyline.Gpu;

/// <summary>
/// Caps how many frames may be submitted to the GPU but not yet finished.
/// Without a cap, a fast CPU runs frames ahead of the GPU and every input
/// waits behind that queue — the cap is what keeps latency flat.
///
/// The contract: call <see cref="Wait"/> before recording a frame, and
/// <see cref="FrameSubmitted"/> right after the frame's QueueSubmit. The
/// default of 2 lets the CPU record one frame while the GPU draws another.
///
/// Steady-state cost per frame: one native call and two interlocked
/// operations. No allocation — the completion callback is created once
/// and reused. Requires the wgpu-native poll extension.
/// </summary>
public sealed unsafe class FramePacer : IDisposable
{
    private readonly GpuContext _context;
    // Created once and rooted for the pacer's lifetime: a per-frame
    // PfnQueueWorkDoneCallback.From(...) would allocate and risk collection
    // while wgpu still holds the pointer.
    private readonly PfnQueueWorkDoneCallback _onWorkDone;
    private int _inFlight;
    private bool _disposed;

    public FramePacer(GpuContext context, int maxFramesInFlight = 2)
    {
        if (maxFramesInFlight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFramesInFlight), "at least one frame must be allowed in flight");
        }
        // Fail before any callback is registered: without polling, Wait
        // could never pump completions and Dispose could never drain, so a
        // registered callback could outlive the pacer.
        if (!context.SupportsPoll)
        {
            Guard.FailPollRequired("frame pacing");
        }

        _context = context;
        MaxFramesInFlight = maxFramesInFlight;
        // Decrement on every status, not just Success: an error or device
        // loss must still free the slot, or Wait would hang forever.
        _onWorkDone = PfnQueueWorkDoneCallback.From((_, _) => Interlocked.Decrement(ref _inFlight));
    }

    public int MaxFramesInFlight { get; }

    /// <summary>Frames submitted but not yet finished on the GPU.</summary>
    public int FramesInFlight => Volatile.Read(ref _inFlight);

    /// <summary>
    /// Block until fewer than <see cref="MaxFramesInFlight"/> frames are in
    /// flight. Each wait round drives the device so completion callbacks
    /// can fire.
    /// </summary>
    public void Wait()
    {
        while (Volatile.Read(ref _inFlight) >= MaxFramesInFlight)
        {
            _context.Poll(wait: true);
        }
    }

    /// <summary>
    /// Non-blocking check. Pumps completions once, then reports whether a
    /// slot is free.
    /// </summary>
    public bool TryWait()
    {
        if (Volatile.Read(ref _inFlight) < MaxFramesInFlight)
        {
            return true;
        }

        _context.Poll(wait: false);
        return Volatile.Read(ref _inFlight) < MaxFramesInFlight;
    }

    /// <summary>
    /// Record that one frame's work was submitted. Call right after the
    /// QueueSubmit that ends the frame.
    /// </summary>
    public void FrameSubmitted()
    {
        Interlocked.Increment(ref _inFlight);
        _context.Api.QueueOnSubmittedWorkDone(_context.QueueHandle, _onWorkDone, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Drain so no callback fires into a disposed pacer. The constructor
        // guarantees polling exists, so this always terminates.
        while (Volatile.Read(ref _inFlight) > 0)
        {
            _context.Poll(wait: true);
        }

        _onWorkDone.Dispose();
    }
}
