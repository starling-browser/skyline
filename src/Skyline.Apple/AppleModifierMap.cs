// SPDX-License-Identifier: Apache-2.0
using AppKit;
using Skyline.Input;

namespace Skyline.Apple;

/// <summary>
/// Maps AppKit's <see cref="NSEventModifierMask"/> to Skyline's
/// <see cref="ModifierKeys"/>. AppKit's Command key is Skyline's
/// <see cref="ModifierKeys.Cmd"/>, matching the GLFW Super key.
/// </summary>
internal static class AppleModifierMap
{
    internal static ModifierKeys Map(NSEventModifierMask flags)
    {
        var mods = ModifierKeys.None;
        if (flags.HasFlag(NSEventModifierMask.ShiftKeyMask))
        {
            mods |= ModifierKeys.Shift;
        }
        if (flags.HasFlag(NSEventModifierMask.ControlKeyMask))
        {
            mods |= ModifierKeys.Ctrl;
        }
        if (flags.HasFlag(NSEventModifierMask.AlternateKeyMask))
        {
            mods |= ModifierKeys.Alt;
        }
        if (flags.HasFlag(NSEventModifierMask.CommandKeyMask))
        {
            mods |= ModifierKeys.Cmd;
        }
        if (flags.HasFlag(NSEventModifierMask.AlphaShiftKeyMask))
        {
            mods |= ModifierKeys.CapsLock;
        }
        return mods;
    }
}
