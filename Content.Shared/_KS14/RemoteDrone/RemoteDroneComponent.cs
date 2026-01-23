// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.RemoteDrone;

/// <summary>
///     A remotely controlled drone.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(RemoteDroneSystem))]
public sealed partial class RemoteDroneComponent : Component
{
    /// <summary>
    ///     Entity with <see cref="RemoteDroneControllerComponent"/>
    ///         linked to this drone.
    /// </summary>
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? LinkedControllerUid = null;

    /// <summary>
    ///     Port to connect to controller.
    /// </summary>
    [DataField, ViewVariables]
    public ProtoId<SinkPortPrototype> SinkPort = "RemoteDroneReceiver";
}
