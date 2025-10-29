// SPDX-FileCopyrightText: 2025 IProduceWidgets
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 metalgearsloth
//
// SPDX-License-Identifier: MPL-2.0

namespace Content.Shared.NodeContainer.NodeGroups;

public enum NodeGroupID : byte
{
    Default,
    HVPower,
    MVPower,
    Apc,
    AMEngine,
    Pipe,
    WireNet,

    /// <summary>
    /// Group used by the TEG.
    /// </summary>
    /// <seealso cref="Content.Server.Power.Generation.Teg.TegSystem"/>
    /// <seealso cref="Content.Server.Power.Generation.Teg.TegNodeGroup"/>
    Teg,
    ExCable,
    Plumbing,
}
