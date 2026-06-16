// SPDX-License-Identifier: Apache-2.0
using Skyline.Input;

namespace Skyline.Apple;

/// <summary>
/// Maps macOS virtual keycodes (<c>kVK_*</c>) to Skyline's <see cref="Key"/>.
/// Unmapped keys report <see cref="Key.Unknown"/>. The event's <c>Code</c> then
/// carries the GLFW-space keycode (<c>(int)Key</c>), not the macOS code, so it
/// matches the GLFW backend for every key Skyline knows.
/// </summary>
internal static class AppleKeyMap
{
    internal static Key Map(ushort keyCode) => keyCode switch
    {
        0 => Key.A, 1 => Key.S, 2 => Key.D, 3 => Key.F, 4 => Key.H, 5 => Key.G,
        6 => Key.Z, 7 => Key.X, 8 => Key.C, 9 => Key.V, 11 => Key.B, 12 => Key.Q,
        13 => Key.W, 14 => Key.E, 15 => Key.R, 16 => Key.Y, 17 => Key.T,
        31 => Key.O, 32 => Key.U, 34 => Key.I, 35 => Key.P, 37 => Key.L,
        38 => Key.J, 40 => Key.K, 45 => Key.N, 46 => Key.M,

        18 => Key.D1, 19 => Key.D2, 20 => Key.D3, 21 => Key.D4, 23 => Key.D5,
        22 => Key.D6, 26 => Key.D7, 28 => Key.D8, 25 => Key.D9, 29 => Key.D0,

        24 => Key.Equal, 27 => Key.Minus, 30 => Key.RightBracket, 33 => Key.LeftBracket,
        39 => Key.Apostrophe, 41 => Key.Semicolon, 42 => Key.Backslash, 43 => Key.Comma,
        44 => Key.Slash, 47 => Key.Period, 50 => Key.GraveAccent,

        36 => Key.Enter, 48 => Key.Tab, 49 => Key.Space, 51 => Key.Backspace,
        53 => Key.Escape, 117 => Key.Delete,

        123 => Key.Left, 124 => Key.Right, 125 => Key.Down, 126 => Key.Up,
        115 => Key.Home, 119 => Key.End, 116 => Key.PageUp, 121 => Key.PageDown,

        55 => Key.LeftSuper, 54 => Key.RightSuper, 56 => Key.LeftShift, 60 => Key.RightShift,
        57 => Key.CapsLock, 58 => Key.LeftAlt, 61 => Key.RightAlt, 59 => Key.LeftControl,
        62 => Key.RightControl,

        122 => Key.F1, 120 => Key.F2, 99 => Key.F3, 118 => Key.F4, 96 => Key.F5,
        97 => Key.F6, 98 => Key.F7, 100 => Key.F8, 101 => Key.F9, 109 => Key.F10,
        103 => Key.F11, 111 => Key.F12,

        _ => Key.Unknown,
    };
}
