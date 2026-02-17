// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Tank;

/// <summary>
///     Because DamageVisualsComponent doesn't support non-enums as a layer key
/// </summary>
[Serializable, NetSerializable]
public enum TankVisualLayers : byte
{
    Base,
    Armor
}
