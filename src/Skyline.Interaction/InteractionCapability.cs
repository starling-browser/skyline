// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>
/// What an actor is allowed to do. A flags set so one grant can carry several
/// powers at once. The three the approvals UI gates on are
/// <see cref="Edit"/> ("wants to type"), <see cref="LowLevelInput"/> ("wants
/// the keyboard"), and <see cref="Administer"/> ("requests control").
/// </summary>
[Flags]
public enum InteractionCapability
{
    None = 0,
    Observe = 1 << 0,
    Point = 1 << 1,
    Focus = 1 << 2,
    Select = 1 << 3,
    Manipulate = 1 << 4,
    Activate = 1 << 5,
    Edit = 1 << 6,
    Transfer = 1 << 7,
    Collaborate = 1 << 8,
    LowLevelInput = 1 << 9,
    Administer = 1 << 10,
    Navigation = 1 << 11,
    Capture = 1 << 12,
}
