// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.OverlayStains;

/// <summary>
///     Component to visualise blood-stains on things.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StainedComponent : Component
{
    /// <summary>
    ///     Stains that are on this entity, with their color,
    ///         with the vector's 2 first elements being its X and Y offset,
    ///         and 3rd element being from 0 to 1 specifying its rotation.
    /// </summary>
    [AutoNetworkedField]
    public List<(Vector3, Color)> Stains = new();

    /// <summary>
    ///     Was a <see cref="Chemistry.Reaction.ReactiveComponent"/> created
    ///         on this entity after being stained?
    /// </summary>
    [AutoNetworkedField]
    public bool OwnsBoundReactiveComponent = false;
}
