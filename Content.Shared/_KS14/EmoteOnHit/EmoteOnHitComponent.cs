// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Chat.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EmoteOnHit;

/// <summary>
///     When attached to a melee weapon,
///         makes the user do an emote
///         every time they hit something with it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class EmoteOnHitComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<EmotePrototype> Emote;

    /// <summary>
    ///     Must the user stand to be forced to emote?
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool RequireStanding = true;
}
