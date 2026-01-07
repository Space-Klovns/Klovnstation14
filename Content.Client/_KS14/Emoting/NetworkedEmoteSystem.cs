// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Client.Chat;
using Content.Shared._KS14.Emoting;
using Content.Shared.Chat;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.Emoting;

/// <summary>
///     Handles <see cref="NetworkedEmoteMessage"/>
///         on the client, converting it to an <see cref="EmoteEvent"/>.  
///
///     This entire system is only done because player-inputted emotes are (right now)
///         only handled on-server and therefore not predicted, so we do this goidafix
///         to make server experience consistent with client.
/// </summary>
public sealed class NetworkedEmoteSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<NetworkedEmoteMessage>(OnNetworkedEmoteMessage);
    }

    private void OnNetworkedEmoteMessage(NetworkedEmoteMessage args)
    {
        if (!_prototypeManager.TryIndex(args.EmoteId, out var emotePrototype))
        {
            DebugTools.Assert(
                $"When handling NetworkedEmoteMessage, could not index any EmotePrototype of ID '{(args.EmoteId.ToString().IsWhiteSpace() ? "[WHITESPACE ID]" : args.EmoteId.ToString())}'");

            return;
        }

        if (!TryGetEntity(args.NetId, out var uid))
        {
            DebugTools.Assert($"When handling NetworkedEmoteMessage, could not find any EntityUid corresponding to NetEntity of {args.NetId}");
            return;
        }

        _chatSystem.ImmediatelyInvokeEmoteEvent(uid.Value, emotePrototype);
    }
}
