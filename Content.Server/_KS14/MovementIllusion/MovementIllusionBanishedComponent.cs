// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;

namespace Content.Server._KS14.MovementIllusion;

/// <summary>
///     These things will move ETERNALLY
/// </summary>
[RegisterComponent]
[UnsavedComponent]
public sealed partial class MovementIllusionBanishedComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan DeleteTime = TimeSpan.MinValue;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 Velocity = Vector2.Zero;
}
