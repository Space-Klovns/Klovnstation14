// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 ScarKy0
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Marker component given to the users of the <see cref="JumpAbilityComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActiveLeaperComponent : Component
{
    /// <summary>
    /// The duration to stun the owner on collide with environment.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? KnockdownDuration = null; // KS14: Made optional

    // KS14 addition
    /// <summary>
    /// If specified, this is how long to stun the owner for if they didn't collide with the
    /// environment after finishing the leap.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? GuaranteedKnockdownDuration = null;

    // KS14 addition
    /// <summary>
    /// If specified, this much stamina damage will be dealt to any hit targets.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StaminaDamage = 0f;
}
