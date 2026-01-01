// SPDX-FileCopyrightText: 2025 Gerkada
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Events;

/// <summary>
/// Raised on a gun when another system has modified its ammo provider,
/// to signal that the gun's appearance needs to be updated.
/// </summary>
[ByRefEvent]
public readonly record struct GunNeedsAppearanceUpdateEvent;
