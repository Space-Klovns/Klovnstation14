// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.Atmos.Components;
using Content.Shared._KS14.Speczones;
using Content.Shared.Damage.Components;
using Content.Shared.Wires;

namespace Content.Server._KS14.Speczones;

// This file handles making speczones invincible-ish

public sealed partial class SpeczoneSystem : SharedSpeczoneSystem
{
    /// <summary>
    ///     Processes invincibility of all speczone entities.
    /// </summary>
    private void ProcessSpeczoneInvincibility()
    {
        var eqe = EntityManager.AllEntityQueryEnumerator<AirtightComponent, DamageableComponent, TransformComponent>();
        while (eqe.MoveNext(out var uid, out var _, out var damageableComponent, out var transformComponent))
        {
            if (transformComponent.MapUid is not { } mapUid ||
                !_speczoneQuery.HasComponent(mapUid))
                continue;

            RemComp(uid, damageableComponent);

            if (_rcdDeconstructableQuery.TryGetComponent(uid, out var rcdDeconstructableComponent))
                RemComp(uid, rcdDeconstructableComponent);

            if (_anchorableQuery.TryGetComponent(uid, out var anchorableComponent))
                RemComp(uid, anchorableComponent);

            if (_doorQuery.HasComponent(uid) &&
                TryComp<WiresPanelComponent>(uid, out var wirePanelComponent))
                RemComp(uid, wirePanelComponent);
        }
    }
}
