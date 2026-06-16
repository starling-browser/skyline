// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;

namespace Skyline;

/// <summary>A window backend paired with the process pump that drives it.</summary>
internal readonly record struct AppleBackend(IWindowBackend Backend, IWindowEventPump Pump);

/// <summary>
/// Picks the windowing backend for a new window and holds the process pump. On
/// macOS it asks the optional <c>Skyline.Apple</c> assembly for a native AppKit
/// backend; everywhere else, and when that assembly is absent or
/// <see cref="AppWindowOptions.ForceGlfw"/> is set, it uses GLFW. Every backend
/// that gets created registers its pump with <see cref="Pump"/>, so a process
/// that mixes backends drives them all rather than freezing the odd one out.
/// </summary>
internal static class WindowBackendFactory
{
    private static readonly CompositeEventPump pump = new();

    /// <summary>
    /// Resolves a native backend, or null to fall back to GLFW. Swappable so a
    /// test can drive the native path without a real macOS window.
    /// </summary>
    internal static Func<AppWindowOptions, AppleBackend?> AppleBackendFactory = LoadAppleBackend;

    internal static IWindowBackend Create(AppWindowOptions options)
    {
        if (!options.ForceGlfw && AppleBackendFactory(options) is { } apple)
        {
            pump.Track(apple.Pump);
            return apple.Backend;
        }
        var backend = new GlfwWindowBackend(options);
        pump.Track(new GlfwEventPump());
        return backend;
    }

    /// <summary>
    /// The process pump. It drives every backend that has a window, so it is
    /// safe to read before any window exists (it simply pumps nothing) and it
    /// never strands a backend whose window was created after another kind's.
    /// </summary>
    internal static IWindowEventPump Pump => pump;

    // The native backend lives in a separate net-macos assembly that the core
    // never references at build time and the portable coverage gate never
    // deploys, so this reflective load cannot be exercised there — like
    // Guard.cs, it is excluded rather than faked. The decision to take this
    // path, and the fallback when it returns null, stay covered through
    // AppleBackendFactory.
    [ExcludeFromCodeCoverage]
    private static AppleBackend? LoadAppleBackend(AppWindowOptions options)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }
        try
        {
            var type = Assembly.Load("Skyline.Apple").GetType("Skyline.Apple.AppKitBackend", throwOnError: true)!;
            var create = type.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
            return (AppleBackend)create.Invoke(null, [options])!;
        }
        catch
        {
            // No Skyline.Apple deployed: run on GLFW instead.
            return null;
        }
    }
}

/// <summary>
/// Drives every backend's process pump at once. Each backend kind (GLFW,
/// AppKit) has a single process-wide event source, so this holds one pump per
/// kind and fans <see cref="PollEvents"/> and <see cref="PostEmptyEvent"/> out
/// to all of them. That removes the first-window-wins coupling: a process that
/// mixes backends, or one woken before its first window, drives them all.
/// </summary>
internal sealed class CompositeEventPump : IWindowEventPump
{
    private readonly Lock _gate = new();
    // The pumps as an immutable array the hot loop reads without a lock or
    // per-tick allocation. Replaced wholesale (never mutated in place) under the
    // lock when a new pump kind is added — rare: window creation.
    private volatile IWindowEventPump[] _pumps = [];

    internal void Track(IWindowEventPump pump)
    {
        lock (_gate)
        {
            foreach (var existing in _pumps)
            {
                if (existing.GetType() == pump.GetType())
                {
                    return;
                }
            }
            _pumps = [.. _pumps, pump];
        }
    }

    // Read the array reference once and pump outside any lock: a backend's event
    // handler can create a window (calling Track), and iterating the immutable
    // array means that never disturbs this loop.
    public void PollEvents()
    {
        foreach (var pump in _pumps)
        {
            pump.PollEvents();
        }
    }

    public void PostEmptyEvent()
    {
        foreach (var pump in _pumps)
        {
            pump.PostEmptyEvent();
        }
    }
}
