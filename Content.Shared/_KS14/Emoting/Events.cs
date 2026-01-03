// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Chat.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Emoting;

/// <summary>
///     Raised server-side by <see cref="Chat.SharedChatSystem"/>
///         to the client, telling it to handle an emote being played. 
/// </summary>
[Serializable, NetSerializable]
public sealed class NetworkedEmoteMessage : EntityEventArgs
{
    public NetworkedEmoteMessage(NetEntity netId, ProtoId<EmotePrototype> emoteId)
    {
        NetId = netId;
        EmoteId = emoteId;
    }

    /// <summary>
    ///     Network UID of the entity doing the emote.
    /// </summary>
    public NetEntity NetId;

    public ProtoId<EmotePrototype> EmoteId;
}
