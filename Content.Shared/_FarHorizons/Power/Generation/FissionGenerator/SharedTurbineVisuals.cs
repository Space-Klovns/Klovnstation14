// SPDX-FileCopyrightText: 2025 jhrushbe
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.Serialization;

namespace Content.Shared._FarHorizons.Power.Generation.FissionGenerator;

[Serializable, NetSerializable]
public enum TurbineVisuals
{
    TurbineRuined,
    DamageSpark,
    DamageSmoke,
    TurbineSpeed,
}

[Serializable, NetSerializable]
public enum TurbineVisualLayers
{
    TurbineRuined,
    DamageSpark,
    DamageSmoke,
    TurbineSpeed,
}

[Serializable, NetSerializable]
public enum TurbineSpeed
{
    SpeedStill,
    SpeedSlow,
    SpeedMid,
    SpeedFast,
    SpeedOverspeed,
}