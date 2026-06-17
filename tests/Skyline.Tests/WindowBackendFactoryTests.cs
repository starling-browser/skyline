using Silk.NET.Core.Contexts;
using Skyline;
using Skyline.Input;

namespace Skyline.Tests;

[TestClass]
public class WindowBackendFactoryTests
{
    // A backend with no real window, standing in for the native macOS one so
    // the AppKit-shaped paths are reachable off a Mac.
#pragma warning disable CS0067 // events are part of the seam but unused by the fake
    private sealed class FakeBackend : IWindowBackend
    {
        public (int Width, int Height) FramebufferSize => (200, 100);
        public (int Width, int Height) LogicalSize => (100, 50);
        public string Title { get; set; } = "fake";
        public string? ClipboardText { get; set; }
        public IReadOnlyList<string> ClipboardFormats => [];
        public byte[]? GetClipboardData(string mimeType) => null;
        public void SetClipboardData(string mimeType, byte[] data) { }
        public bool IsClosing => false;
        public WindowSurfaceSource SurfaceSource { get; } = new WindowSurfaceSource.MetalLayer(0x1234);

        public event Action<(int Width, int Height)>? FramebufferResized;
        public event Action<bool>? MinimizedChanged;
        public event Action<double>? Render;
        public event Action<PointerEvent>? Pointer;
        public event Action<KeyEvent>? Key;
        public event Action<TextEvent>? Text;

        public void Run() { }
        public void PumpEventsOnce() { }
        public void RequestClose() { }
        public void Resize(int width, int height) { }
        public void Minimize() { }
        public void Restore() { }
        public void Dispose() { }
    }

    private sealed class FakePump : IWindowEventPump
    {
        public void PollEvents() { }
        public void PostEmptyEvent() { }
    }
#pragma warning restore CS0067

    private class CountingPumpA : IWindowEventPump
    {
        public int Polls { get; private set; }
        public int Wakes { get; private set; }
        public void PollEvents() => Polls++;
        public void PostEmptyEvent() => Wakes++;
    }

    // A distinct runtime type, standing in for the other backend's pump.
    private sealed class CountingPumpB : CountingPumpA;

    [TestMethod]
    public void CompositePump_FansOutPerKind_AndDedupesByType()
    {
        var pump = new CompositeEventPump();
        var a = new CountingPumpA();
        var b = new CountingPumpB();
        var aDuplicate = new CountingPumpA();
        pump.Track(a);
        pump.Track(b);          // a distinct kind: driven alongside a
        pump.Track(aDuplicate); // same kind as a: one pump per kind, so ignored

        pump.PollEvents();
        pump.PostEmptyEvent();

        Assert.AreEqual(1, a.Polls);
        Assert.AreEqual(1, b.Polls, "a mixed second backend is still pumped");
        Assert.AreEqual(0, aDuplicate.Polls, "a second pump of the same kind is not tracked");
        Assert.AreEqual(1, a.Wakes);
        Assert.AreEqual(1, b.Wakes);
    }

    [TestMethod]
    public void NativeBackendExposesMetalLayerAndDpr()
    {
        var prev = WindowBackendFactory.AppleBackendFactory;
        try
        {
            WindowBackendFactory.AppleBackendFactory = _ => new AppleBackend(new FakeBackend(), new FakePump());
            using var win = new AppWindow(new AppWindowOptions());
            Assert.AreEqual((nint)0x1234, win.MetalLayer);
            // 200px framebuffer over 100pt logical width is a 2x backing scale.
            Assert.AreEqual(2f, win.CurrentFrame.Dpr);
        }
        finally
        {
            WindowBackendFactory.AppleBackendFactory = prev;
        }
    }

    [TestMethod]
    public void SurfaceThrowsOnNativeBackend()
    {
        var prev = WindowBackendFactory.AppleBackendFactory;
        try
        {
            WindowBackendFactory.AppleBackendFactory = _ => new AppleBackend(new FakeBackend(), new FakePump());
            using var win = new AppWindow(new AppWindowOptions());
            Assert.ThrowsException<InvalidOperationException>(() => _ = win.Surface);
        }
        finally
        {
            WindowBackendFactory.AppleBackendFactory = prev;
        }
    }
}
