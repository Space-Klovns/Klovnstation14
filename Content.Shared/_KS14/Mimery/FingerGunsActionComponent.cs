// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MIT

namespace Content.Shared._KS14.Mimery;

[RegisterComponent]
public sealed partial class FingerGunsActionComponent : Component
{
    /// <summary>
    /// How many times the spell can be casted without cooldown resetting
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public int UsesLeft = 3;

    /// <summary>
    /// Max uses for this spell before it's cooldown is reset
    /// </summary>
    [DataField]
    public int CastAmount = 3;

    /// <summary>
    /// This determines spell use delay, not action component
    /// </summary>
    [DataField]
    public TimeSpan UseDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// This determines fire delay between firing bullets
    /// </summary>
    [DataField]
    public TimeSpan FireDelay = TimeSpan.FromMilliseconds(250);
}
