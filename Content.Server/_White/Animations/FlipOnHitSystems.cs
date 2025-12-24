// SPDX-FileCopyrightText: 2024 Aviu00
// SPDX-FileCopyrightText: 2024 Preston Smith
// SPDX-FileCopyrightText: 2024 Spatison
// SPDX-FileCopyrightText: 2025 Aiden
// SPDX-FileCopyrightText: 2025 FrauzJ
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._White.Animations;
using Robust.Shared.Player;

namespace Content.Server._White.Animations;

public sealed class FlipOnHitSystem : SharedFlipOnHitSystem
{
    protected override void PlayAnimation(EntityUid user)
    {
        var filter = Filter.Pvs(user, entityManager: EntityManager);

        if (TryComp<ActorComponent>(user, out var actor))
            filter.RemovePlayer(actor.PlayerSession);

        RaiseNetworkEvent(new FlipOnHitEvent(GetNetEntity(user)), filter);
    }
}
