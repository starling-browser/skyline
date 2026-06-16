// SPDX-License-Identifier: Apache-2.0

namespace Skyline.Render;

/// <summary>A clean run: the number of frames that reached the screen.</summary>
public record Ok(long Frames);

/// <summary>A faulted run: the exception a render callback threw.</summary>
public record Err(Exception Error);

/// <summary>
/// How a run ended, as a closed set of two cases. Exactly one is ever true:
/// <see cref="Ok"/> when the loop ran clean, or <see cref="Err"/> when a draw
/// callback threw. Branch on it instead of calling a throw-or-not method.
/// </summary>
public union FrameOutcome(Ok, Err);
