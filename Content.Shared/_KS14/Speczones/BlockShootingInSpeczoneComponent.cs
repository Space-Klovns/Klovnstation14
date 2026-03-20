// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Speczones;

/// <summary>
///     Added to guns to block them from being fired when in speczones.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BlockShootingInSpeczoneComponent : Component;
