// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MIT

using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Mobs.Components;

[RegisterComponent, NetworkedComponent]
[Access(typeof(MobThresholdSystem))]
public sealed partial class ModifiedMobThresholdsStatusEffectComponent : Component
{
    /// <summary>
    ///     Value to add to each threshold.
    ///         Does nothing for the mobstate if a threshold for it doesnt exist.
    /// </summary>
    [DataField]
    public Dictionary<MobState, FixedPoint2> ThresholdAdjustments = [];
}
