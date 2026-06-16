// SPDX-License-Identifier: Apache-2.0
using AppKit;
using CoreGraphics;
using Foundation;

namespace Skyline.Apple;

/// <summary>
/// The entry point the core loads by reflection on macOS. It brings up the
/// shared <c>NSApplication</c> once, then hands back a window backend and the
/// process pump.
/// </summary>
internal static class AppKitBackend
{
    private static AppKitEventPump? _pump;

    internal static AppleBackend Create(AppWindowOptions options)
    {
        var pump = EnsureApp();
        return new AppleBackend(new AppKitWindowBackend(options, pump), pump);
    }

    private static AppKitEventPump EnsureApp()
    {
        if (_pump is not null)
        {
            return _pump;
        }

        // No NSApplication.Main here: Skyline apps run a plain Main that drives
        // AppWindow.Run or AppHost.Run, so bring AppKit up by hand and pump it
        // ourselves. Regular activation policy gives a real menu and Dock app.
        NSApplication.Init();
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        app.FinishLaunching();
        // Bring the app forward when launched from a terminal. The newer
        // argument-less Activate() is macOS 14+ only, so keep this for 12.0.
#pragma warning disable CA1422
        app.ActivateIgnoringOtherApps(true);
#pragma warning restore CA1422

        _pump = new AppKitEventPump();
        return _pump;
    }
}

/// <summary>
/// The macOS pump. <see cref="PollEvents"/> drains the queue without blocking,
/// matching GLFW's poll-and-sleep contract so <c>AppHost</c> keeps its main-
/// thread cadence. It never calls <c>NSApplication.Run</c>, which would block.
/// </summary>
internal sealed class AppKitEventPump : IWindowEventPump
{
    public void PollEvents()
    {
        var app = NSApplication.SharedApplication;
        while (true)
        {
            var ev = app.NextEvent(NSEventMask.AnyEvent, NSDate.DistantPast, NSRunLoopMode.Default, true);
            if (ev is null)
            {
                break;
            }
            app.SendEvent(ev);
        }
    }

    public void PostEmptyEvent()
    {
        var wake = NSEvent.OtherEvent(
            NSEventType.ApplicationDefined,
            CGPoint.Empty,
            0,
            0,
            0,
            null,
            0,
            0,
            0);
        NSApplication.SharedApplication.PostEvent(wake, atStart: true);
    }
}
