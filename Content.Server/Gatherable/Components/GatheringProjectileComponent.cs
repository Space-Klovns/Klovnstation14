// SPDX-FileCopyrightText: 2023 AJCM-git
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2023 metalgearsloth
// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MIT

namespace Content.Server.Gatherable.Components;

/// <summary>
/// Destroys a gatherable entity when colliding with it.
/// </summary>
[RegisterComponent]
public sealed partial class GatheringProjectileComponent : Component
{
    /// <summary>
    /// How many more times we can gather.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("amount")]
    public int Amount = 1;

    // KS14
    /// <summary>
    ///     Is this entity deleted immediately upon making contact with
    ///         something that can't be gathered?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool DeleteOnUngatherable = true;
}
