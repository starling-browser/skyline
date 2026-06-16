// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>A point on a surface, in logical pixels.</summary>
public sealed record PointTarget(float X, float Y);

/// <summary>A whole object, named by id.</summary>
public sealed record ObjectTarget(string ObjectId);

/// <summary>A span inside an object — for example a text selection.</summary>
public sealed record RangeTarget(string ObjectId, int Start, int End);

/// <summary>A ray cast into the scene, for gaze or a pointing device in 3D.</summary>
public sealed record RayTarget(
    float OriginX, float OriginY, float OriginZ,
    float DirectionX, float DirectionY, float DirectionZ);

/// <summary>An axis-aligned box in space.</summary>
public sealed record VolumeTarget(
    float X, float Y, float Z, float Width, float Height, float Depth);

/// <summary>A target named by a semantic address rather than geometry — a URI an AI or a command can resolve.</summary>
public sealed record SemanticTarget(string Uri);

/// <summary>
/// What an interaction is aimed at, as a closed set of cases. Geometry
/// (point, ray, volume), structure (object, range), or meaning (semantic).
/// </summary>
public union Target(PointTarget, ObjectTarget, RangeTarget, RayTarget, VolumeTarget, SemanticTarget);

/// <summary>
/// A target plus the surface it belongs to. The focus slots and command
/// requests carry these, so "where" always names "where on what".
/// </summary>
public sealed record TargetRef(string SurfaceId, Target Target);
