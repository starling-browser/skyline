// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>How an input arrived. New devices use <see cref="Other"/> with a derived source.</summary>
public enum InputModality
{
    Pointer,
    Keyboard,
    Touch,
    Voice,
    Gaze,
    Clipboard,
    Ai,
    Other,
}

/// <summary>A plain source with no extra state — a mouse, a key, a touch.</summary>
public sealed record GenericSourceState(InputModality Modality);

/// <summary>Spoken input: the recognized text and how sure the recognizer is.</summary>
public sealed record VoiceSourceState(string Transcript, float Confidence);

/// <summary>Eye input: the gaze point in logical pixels and how sure the tracker is.</summary>
public sealed record GazeSourceState(float X, float Y, float Confidence);

/// <summary>The state of whichever source produced an input, as a closed set.</summary>
public union SourceState(GenericSourceState, VoiceSourceState, GazeSourceState);

/// <summary>One reading from one source at one moment: who, which modality, its state, and when.</summary>
public sealed record InputSnapshot(Actor Source, InputModality Modality, SourceState State, DateTimeOffset At);

/// <summary>
/// A resolved intent to run a command: the command, the actor behind it, where
/// it lands, and the input that evidenced it. The unit an interaction shell
/// authorizes and dispatches.
/// </summary>
public sealed record InteractionIntent(
    CommandId Command,
    Actor Actor,
    TargetRef? Target = null,
    InputSnapshot? Evidence = null);
