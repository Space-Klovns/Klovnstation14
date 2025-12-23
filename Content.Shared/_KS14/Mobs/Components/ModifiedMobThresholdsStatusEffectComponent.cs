// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
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
    [DataField]
    public SortedDictionary<FixedPoint2, MobState> NewThresholds = [];

    [DataField]
    public SortedDictionary<FixedPoint2, MobState> OldThresholds = [];
}
