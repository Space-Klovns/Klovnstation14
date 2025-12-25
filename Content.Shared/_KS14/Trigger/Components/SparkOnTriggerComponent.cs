// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Trigger.Components.Effects;

// TODO: Hotspot-expose
/// <summary>
///     Spawns sparks upon being triggered.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class SparkOnTriggerComponent : BaseXOnTriggerComponent
{
    /// <summary>
    ///     Range of minimum and maximum number of sparks to emit upon trigger.
    /// </summary>
    [DataField]
    public Vector2i CountRange = new(3, 5);

    /// <summary>
    ///     Range of minimum and maximum velocity of sparks emitted upon trigger.
    /// </summary>
    [DataField]
    public Vector2 VelocityRange = new(2.5f, 5f);

    /// <summary>
    ///     Prototype to spawn as sparks.
    /// </summary>
    [DataField]
    public EntProtoId Prototype = "EffectSparks";

    /// <summary>
    ///     Sound to play upon trigger, at position of sparks.
    /// </summary>
    [DataField]
    public SoundSpecifier? SoundSpecifier = null;
}

