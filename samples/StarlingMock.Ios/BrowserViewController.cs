// SPDX-License-Identifier: Apache-2.0
using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace StarlingMock;

/// <summary>
/// The browser shell: native UIKit chrome (address bar) over a wgpu-rendered
/// page. The chrome is real platform UI; only the page is Starling's.
/// </summary>
public sealed class BrowserViewController : UIViewController
{
    private static string HomeUrl
    {
        get
        {
            // Like a desktop browser, a start URL can come in from outside.
            // On the simulator: SIMCTL_CHILD_STARLING_URL=starling://docs
            // in the environment of `xcrun simctl launch`.
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "--url");
            if (i >= 0 && i + 1 < args.Length)
            {
                return args[i + 1];
            }
            return Environment.GetEnvironmentVariable("STARLING_URL") ?? "starling://home";
        }
    }

    private MetalView _pageView = null!;
    private UITextField _addressBar = null!;
    private PageRenderer? _renderer;
    private CADisplayLink? _link;
    private double _lastTimestamp;
    private float _lastPanY;
    private float _flingVelocity;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;

        var bar = new UIView { TranslatesAutoresizingMaskIntoConstraints = false };
        _addressBar = new UITextField
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Text = HomeUrl,
            BorderStyle = UITextBorderStyle.RoundedRect,
            KeyboardType = UIKeyboardType.Url,
            AutocapitalizationType = UITextAutocapitalizationType.None,
            AutocorrectionType = UITextAutocorrectionType.No,
            ReturnKeyType = UIReturnKeyType.Go,
            ClearButtonMode = UITextFieldViewMode.WhileEditing,
            Font = UIFont.GetMonospacedSystemFont(15, UIFontWeight.Regular),
        };
        _addressBar.ShouldReturn = field =>
        {
            _renderer?.Navigate(field.Text ?? "");
            field.ResignFirstResponder();
            return false;
        };
        _pageView = new MetalView { TranslatesAutoresizingMaskIntoConstraints = false };
        _pageView.AddGestureRecognizer(new UIPanGestureRecognizer(OnPan));

        bar.AddSubview(_addressBar);
        View.AddSubview(_pageView);
        View.AddSubview(bar);

        var safe = View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            bar.TopAnchor.ConstraintEqualTo(safe.TopAnchor),
            bar.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            bar.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            bar.HeightAnchor.ConstraintEqualTo(52),

            _addressBar.CenterYAnchor.ConstraintEqualTo(bar.CenterYAnchor),
            _addressBar.LeadingAnchor.ConstraintEqualTo(bar.LeadingAnchor, 12),
            _addressBar.TrailingAnchor.ConstraintEqualTo(bar.TrailingAnchor, -12),
            _addressBar.HeightAnchor.ConstraintEqualTo(38),

            _pageView.TopAnchor.ConstraintEqualTo(bar.BottomAnchor),
            _pageView.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            _pageView.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            _pageView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
        });
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        var scale = _pageView.ContentScaleFactor;
        var width = (int)(_pageView.Bounds.Width * scale);
        var height = (int)(_pageView.Bounds.Height * scale);
        if (width <= 0 || height <= 0)
        {
            return;
        }
        _pageView.MetalLayer.DrawableSize = new CGSize(width, height);
        if (_renderer is null)
        {
            _renderer = new PageRenderer(_pageView.MetalLayer.Handle);
            _renderer.Navigate(_addressBar.Text ?? "");
            _link = CADisplayLink.Create(OnFrame);
            _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Default);
        }
        _renderer.Resize(width, height);
    }

    private void OnFrame()
    {
        var now = _link!.Timestamp;
        var dt = _lastTimestamp > 0 ? now - _lastTimestamp : 1.0 / 60.0;
        _lastTimestamp = now;

        if (Math.Abs(_flingVelocity) > 1f)
        {
            _renderer!.ScrollBy(_flingVelocity * (float)dt);
            _flingVelocity *= (float)Math.Exp(-3.0 * dt);
        }
        _renderer!.RenderFrame(dt);
    }

    private void OnPan(UIPanGestureRecognizer pan)
    {
        var scale = (float)_pageView.ContentScaleFactor;
        if (pan.State == UIGestureRecognizerState.Began)
        {
            _flingVelocity = 0f;
            _lastPanY = 0f;
        }
        var y = (float)pan.TranslationInView(_pageView).Y;
        _renderer?.ScrollBy((_lastPanY - y) * scale);
        _lastPanY = y;
        if (pan.State == UIGestureRecognizerState.Ended)
        {
            _flingVelocity = -(float)pan.VelocityInView(_pageView).Y * scale;
        }
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
