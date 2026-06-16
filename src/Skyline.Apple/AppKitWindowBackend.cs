// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using AppKit;
using CoreGraphics;
using Foundation;
using Skyline.Input;

namespace Skyline.Apple;

/// <summary>
/// A native macOS window: an <c>NSWindow</c> with real chrome and a
/// <c>CAMetalLayer</c>-backed content view. <see cref="ChromeMode"/> becomes a
/// style mask and, for <see cref="ChromeMode.Transparent"/>, a see-through
/// title bar with content drawn under it. Geometry, dirty pacing, and resize
/// parking stay in <c>AppWindow</c>; this class just raises the raw events.
/// </summary>
internal sealed class AppKitWindowBackend : IWindowBackend
{
    // The UTI for plain text on the pasteboard (NSPasteboardTypeString).
    private const string PasteboardText = "public.utf8-plain-text";

    private readonly NSWindow _window;
    private readonly MetalView _view;
    private readonly WindowDelegate _delegate;
    private readonly AppKitEventPump _pump;
    private readonly WindowSurfaceSource _surfaceSource;
    private bool _closing;

    public event Action<(int Width, int Height)>? FramebufferResized;
    public event Action<bool>? MinimizedChanged;
    public event Action<double>? Render;
    public event Action<PointerEvent>? Pointer;
    public event Action<KeyEvent>? Key;
    public event Action<TextEvent>? Text;

    internal AppKitWindowBackend(AppWindowOptions options, AppKitEventPump pump)
    {
        _pump = pump;

        var chrome = AppKitChromeMap.Map(options.EffectiveChrome);
        var rect = new CGRect(0, 0, options.Width, options.Height);
        _window = new NSWindow(rect, (NSWindowStyle)chrome.StyleMask, NSBackingStore.Buffered, false)
        {
            Title = options.Title,
        };
        if (chrome.TitlebarTransparent)
        {
            _window.TitlebarAppearsTransparent = true;
        }
        if (chrome.HideTitle)
        {
            _window.TitleVisibility = NSWindowTitleVisibility.Hidden;
        }
        if (!chrome.Opaque)
        {
            _window.IsOpaque = false;
            _window.BackgroundColor = NSColor.Clear;
        }

        _view = new MetalView(rect);
        _window.ContentView = _view;
        _window.MakeFirstResponder(_view);
        if (!chrome.Opaque)
        {
            // Let the clear alpha show what's behind the window. The surface's
            // CompositeAlphaMode still has to opt in (PostMultiplied).
            _view.MetalLayer.Opaque = false;
        }
        _surfaceSource = new WindowSurfaceSource.MetalLayer(_view.MetalLayer.Handle);
        UpdateDrawableSize();

        _view.Pointer += e => Pointer?.Invoke(e);
        _view.Key += e => Key?.Invoke(e);
        _view.Text += e => Text?.Invoke(e);
        _view.Resized += () =>
        {
            UpdateDrawableSize();
            FramebufferResized?.Invoke(FramebufferSize);
        };

        _delegate = new WindowDelegate(this);
        _window.Delegate = _delegate;
        _window.MakeKeyAndOrderFront(null);
    }

    private float Scale => (float)_window.BackingScaleFactor;

    private void UpdateDrawableSize()
    {
        var (w, h) = FramebufferSize;
        var layer = _view.MetalLayer;
        layer.ContentsScale = Scale;
        layer.DrawableSize = new CGSize(w, h);
    }

    public (int Width, int Height) FramebufferSize
    {
        get
        {
            var b = _view.Bounds;
            var scale = Scale;
            return ((int)(b.Width * scale), (int)(b.Height * scale));
        }
    }

    public (int Width, int Height) LogicalSize
    {
        get
        {
            var b = _view.Bounds;
            return ((int)b.Width, (int)b.Height);
        }
    }

    public double RefreshRate => (_window.Screen ?? NSScreen.MainScreen)?.MaximumFramesPerSecond ?? 60;

    public string Title
    {
        get => _window.Title;
        set => _window.Title = value;
    }

    public string? ClipboardText
    {
        get => NSPasteboard.GeneralPasteboard.GetStringForType(PasteboardText);
        set
        {
            var pb = NSPasteboard.GeneralPasteboard;
            pb.ClearContents();
            pb.SetStringForType(value ?? string.Empty, PasteboardText);
        }
    }

    public bool IsClosing => _closing;

    public WindowSurfaceSource SurfaceSource => _surfaceSource;

    public void Run()
    {
        // The solo loop GLFW gives through IWindow.Run: pump events, raise a
        // frame tick. AppWindow throttles idle frames; present paces busy ones.
        var clock = Stopwatch.StartNew();
        var last = 0.0;
        while (!_closing)
        {
            _pump.PollEvents();
            var now = clock.Elapsed.TotalSeconds;
            Render?.Invoke(now - last);
            last = now;
        }
    }

    public void PumpEventsOnce() => _pump.PollEvents();

    public void RequestClose() => _window.Close();

    public void Resize(int width, int height) => _window.SetContentSize(new CGSize(width, height));

    public void Minimize() => _window.Miniaturize(null);

    public void Restore() => _window.Deminiaturize(null);

    internal void OnClosing() => _closing = true;

    internal void OnMinimizedChanged(bool minimized) => MinimizedChanged?.Invoke(minimized);

    public void Dispose()
    {
        _window.Delegate = null;
        if (!_closing)
        {
            _window.Close();
        }
        _view.Dispose();
        _delegate.Dispose();
        _window.Dispose();
    }

    private sealed class WindowDelegate(AppKitWindowBackend backend) : NSWindowDelegate
    {
        public override void WillClose(NSNotification notification) => backend.OnClosing();

        public override void DidMiniaturize(NSNotification notification) => backend.OnMinimizedChanged(true);

        public override void DidDeminiaturize(NSNotification notification) => backend.OnMinimizedChanged(false);
    }
}
