// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Physics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ComplexShove;

/// <summary>
///     Added to entities that can wallshoves, etc..
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ComplexShoveComponent : Component
{
    /// <summary>
    ///     Fraction of stamina crit threshold of a shoved person,
    ///         that is the amount of stamina damage done to them when wallshoving.
    ///
    ///     E.g. if this is 0.25 (25%), then you would only need to
    ///         wallshove ANYTHING 4 times to stamcrit (assumning no stamregen)
    /// </summary>
    [DataField, ViewVariables]
    public float WallshoveStaminaDamageFraction = 0.5f;

    /// <summary>
    ///     Fraction of stamina crit threshold of a shoved person,
    ///         that is the amount of stamina damage done to them when doing a basic shove.
    ///
    ///     E.g. if this is 0.25 (25%), then you would only need to
    ///         basicshove ANYTHING 4 times to stamcrit (assumning no stamregen)
    /// </summary>
    [DataField, ViewVariables]
    public float BasicShoveStaminaDamageFraction = 0.05f;

    /// <summary>
    ///     Maximum distance from a person getting shoved to a wall
    ///         or some other solid object, for the wallshove to be counted.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float WallshoveRange = 1.8f;

    /// <summary>
    ///     Collision *mask* used in the raycast to detect if a wallshove
    ///         should be applied.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public long WallshoveCollisionMask = (long)CollisionGroup.Impassable;

    /// <summary>
    ///     Push force applied to targets who are standing.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float StandingPushPower = 800f;

    /// <summary>
    ///     Push force applied to targets who are knocked down or incapacitated.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DownedPushPower = 950f;
}
