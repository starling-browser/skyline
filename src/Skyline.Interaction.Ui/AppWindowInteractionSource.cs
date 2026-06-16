// SPDX-License-Identifier: Apache-2.0
using Skyline.Input;

namespace Skyline.Interaction.Ui;

/// <summary>
/// The desktop input bridge: it mints the local human actor, turns a
/// pointer-down into an approval gesture through the sink, and reads and writes
/// the clipboard through an injectable <see cref="IClipboard"/>. Feed it pointer
/// events with <see cref="OnPointerEvent"/>.
/// </summary>
public sealed class AppWindowInteractionSource
{
    private readonly IPointerApprovalSink _sink;
    private readonly IClipboard _clipboard;
    private string? _lastCopied;

    public AppWindowInteractionSource(IPointerApprovalSink sink, IClipboard? clipboard = null)
    {
        _sink = sink;
        _clipboard = clipboard ?? new MemoryClipboard();
    }

    /// <summary>The local person at this window. Real input never fakes an AI or remote actor as this.</summary>
    public Actor LocalHuman { get; } = new("local-human", "You", ActorKind.Human, ActorLocality.Local);

    /// <summary>Route one window pointer event. A down answers the front approval; everything else is ignored.</summary>
    public void OnPointerEvent(PointerEvent e)
    {
        if (e.Kind != PointerEventKind.Down)
        {
            return;
        }
        _sink.OnPointerDown(e.X, e.Y);
    }

    /// <summary>Copy text to the clipboard, remembering it so a later paste survives an empty OS clipboard.</summary>
    public void Copy(string text)
    {
        _lastCopied = text;
        _clipboard.Text = text;
    }

    /// <summary>Read the clipboard, falling back to the last text this source copied when the OS clipboard is empty.</summary>
    public string? Paste() => _clipboard.Text ?? _lastCopied;
}

/// <summary>An <see cref="IClipboard"/> backed by a window's native clipboard.</summary>
public sealed class AppWindowClipboard(AppWindow window) : IClipboard
{
    public string? Text
    {
        get => window.ClipboardText;
        set => window.ClipboardText = value;
    }
}
