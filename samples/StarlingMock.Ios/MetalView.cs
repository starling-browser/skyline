// SPDX-License-Identifier: Apache-2.0
using CoreAnimation;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace StarlingMock;

/// <summary>A view backed by a CAMetalLayer — the surface wgpu renders into.</summary>
public sealed class MetalView : UIView
{
    [Export("layerClass")]
    public static Class GetLayerClass() => new(typeof(CAMetalLayer));

    public CAMetalLayer MetalLayer => (CAMetalLayer)Layer;

    public override void MovedToWindow()
    {
        base.MovedToWindow();
        // Render at the panel's native pixel scale, not logical points.
        ContentScaleFactor = Window?.Screen.Scale ?? 1;
    }
}
