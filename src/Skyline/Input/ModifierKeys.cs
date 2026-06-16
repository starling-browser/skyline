// SPDX-License-Identifier: Apache-2.0

namespace Skyline.Input;

/// <summary>
/// Keyboard modifiers held when an input event fired. <see cref="Cmd"/> is the
/// Command key on macOS and the Super (Windows) key on GLFW, so chord handling
/// reads the same flag on every platform.
/// </summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Cmd = 1 << 3,
    CapsLock = 1 << 4,
}
