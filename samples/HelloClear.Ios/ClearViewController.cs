// SPDX-License-Identifier: Apache-2.0
using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace HelloClear;

/// <summary>
/// Owns the metal view, the renderer, and the CADisplayLink frame loop —
/// the iOS stand-in for Skyline's window host and FrameLoop.
/// </summary>
public sealed class ClearViewController : UIViewController
{
    private ClearRenderer? _renderer;
    private CADisplayLink? _link;

    public override void LoadView()
    {
        View = new MetalView();
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        var view = (MetalView)View!;
        var scale = view.ContentScaleFactor;
        var width = (int)(view.Bounds.Width * scale);
        var height = (int)(view.Bounds.Height * scale);
        if (width <= 0 || height <= 0)
        {
            return;
        }
        view.MetalLayer.DrawableSize = new CGSize(width, height);
        if (_renderer is null)
        {
            _renderer = new ClearRenderer(view.MetalLayer.Handle);
            _link = CADisplayLink.Create(() => _renderer!.RenderFrame());
            _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Default);
        }
        _renderer.Resize(width, height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _link?.Invalidate();
            _renderer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
