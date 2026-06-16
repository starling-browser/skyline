// SPDX-License-Identifier: Apache-2.0
using SilkKey = Silk.NET.Input.Key;

namespace Skyline.Input;

/// <summary>
/// Builds <see cref="ModifierKeys"/> by probing live key state. GLFW's input
/// callbacks carry no modifier field, so the backend samples the keyboard with
/// this on each event. Kept pure — a key-pressed predicate in, flags out — so
/// the bit mapping is tested without a window.
/// </summary>
internal static class ModifierKeysMap
{
    internal static ModifierKeys FromPressed(Func<SilkKey, bool> isPressed)
    {
        var mods = ModifierKeys.None;
        if (isPressed(SilkKey.ShiftLeft) || isPressed(SilkKey.ShiftRight))
        {
            mods |= ModifierKeys.Shift;
        }
        if (isPressed(SilkKey.ControlLeft) || isPressed(SilkKey.ControlRight))
        {
            mods |= ModifierKeys.Ctrl;
        }
        if (isPressed(SilkKey.AltLeft) || isPressed(SilkKey.AltRight))
        {
            mods |= ModifierKeys.Alt;
        }
        if (isPressed(SilkKey.SuperLeft) || isPressed(SilkKey.SuperRight))
        {
            mods |= ModifierKeys.Cmd;
        }
        if (isPressed(SilkKey.CapsLock))
        {
            mods |= ModifierKeys.CapsLock;
        }
        return mods;
    }
}
