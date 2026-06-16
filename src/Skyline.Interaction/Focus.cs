// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>
/// The split focus graph: six independent slots, one per kind of attention.
/// Eyes drive <see cref="Attention"/>, a mouse drives <see cref="Pointer"/>, a
/// keyboard drives <see cref="Text"/>, and so on — they never fight over one
/// "focused element". Immutable: every update returns a new graph, so a single
/// writer can swap it atomically.
/// </summary>
public sealed record FocusGraph(
    TargetRef? Attention = null,
    TargetRef? Pointer = null,
    TargetRef? Navigation = null,
    TargetRef? Text = null,
    TargetRef? Command = null,
    TargetRef? Capture = null)
{
    public static readonly FocusGraph Empty = new();

    /// <summary>
    /// Where a semantic command lands when it names no target of its own:
    /// the most specific live slot, in the order command, text, pointer,
    /// attention, navigation.
    /// </summary>
    public TargetRef? BestCommandTarget => Command ?? Text ?? Pointer ?? Attention ?? Navigation;

    /// <summary>
    /// Fold one input source's target into the slot it owns. Gaze touches only
    /// <see cref="Attention"/>; a pointer or touch only <see cref="Pointer"/>;
    /// voice and AI only <see cref="Command"/>; the keyboard only
    /// <see cref="Text"/>. An unmapped modality leaves the graph unchanged.
    /// </summary>
    public FocusGraph ObserveFrom(InputModality modality, TargetRef? target) => modality switch
    {
        InputModality.Gaze => this with { Attention = target },
        InputModality.Pointer or InputModality.Touch => this with { Pointer = target },
        InputModality.Keyboard => this with { Text = target },
        InputModality.Voice or InputModality.Ai or InputModality.Clipboard => this with { Command = target },
        _ => this,
    };
}
