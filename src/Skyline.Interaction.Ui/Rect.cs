// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction.Ui;

/// <summary>An axis-aligned rectangle in logical pixels. The same value both hit-tests a pointer and lays out a quad.</summary>
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public bool Contains(float px, float py) =>
        px >= X && px < X + Width && py >= Y && py < Y + Height;
}
