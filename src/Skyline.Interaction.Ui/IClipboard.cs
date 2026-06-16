// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction.Ui;

/// <summary>The clipboard seam. Text only.</summary>
public interface IClipboard
{
    string? Text { get; set; }
}

/// <summary>An in-process clipboard, used as the fallback when no OS clipboard is wired.</summary>
public sealed class MemoryClipboard : IClipboard
{
    public string? Text { get; set; }
}
