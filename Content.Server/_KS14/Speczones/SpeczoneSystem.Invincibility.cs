// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.Atmos.Components;
using Content.Server.Construction.Components;
using Content.Shared._KS14.Speczones;
using Content.Shared.Construction.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Doors.Components;
using Content.Shared.RCD.Components;
using Content.Shared.Wires;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.Speczones;

// This file handles making speczones invincible-ish
// This is fucking insane

public sealed partial class SpeczoneSystem : SharedSpeczoneSystem
{
    /// <summary>
    ///     Recursively processes invincibility of all the entities on the grids specified.
    /// </summary>
    private void StartInvincibilityProcessingHierarchy(HashSet<Entity<MapGridComponent>> grids)
    {
        var airtightQuery = GetEntityQuery<AirtightComponent>();
        var damageableQuery = GetEntityQuery<DamageableComponent>();
        var rcdDeconstructableQuery = GetEntityQuery<RCDDeconstructableComponent>();
        var constructionQuery = GetEntityQuery<ConstructionComponent>();
        var anchorableQuery = GetEntityQuery<AnchorableComponent>();
        var doorQuery = GetEntityQuery<DoorComponent>();

        foreach (var grid in grids)
        {
            var enumerator = Transform(grid).ChildEnumerator;
            while (enumerator.MoveNext(out var uid))
                RecursivelyProcessEntityInvincibility(
                    uid,
                    airtightQuery,
                    damageableQuery,
                    rcdDeconstructableQuery,
                    constructionQuery,
                    anchorableQuery,
                    doorQuery
                );
        }
    }

    private void RecursivelyProcessEntityInvincibility(
        EntityUid parentUid,
        EntityQuery<AirtightComponent> airtightQuery,
        EntityQuery<DamageableComponent> damageableQuery,
        EntityQuery<RCDDeconstructableComponent> rcdDeconstructableQuery,
        EntityQuery<ConstructionComponent> constructionQuery,
        EntityQuery<AnchorableComponent> anchorableQuery,
        EntityQuery<DoorComponent> doorQuery)
    {
        ProcessEntityInvincibility(
            parentUid,
            airtightQuery,
            damageableQuery,
            rcdDeconstructableQuery,
            constructionQuery,
            anchorableQuery,
            doorQuery
        );

        var enumerator = Transform(parentUid).ChildEnumerator;
        while (enumerator.MoveNext(out var uid))
            RecursivelyProcessEntityInvincibility(
                uid,
                airtightQuery,
                damageableQuery,
                rcdDeconstructableQuery,
                constructionQuery,
                anchorableQuery,
                doorQuery
            );
    }

    private void ProcessEntityInvincibility(
        EntityUid uid,
        EntityQuery<AirtightComponent> airtightQuery,
        EntityQuery<DamageableComponent> damageableQuery,
        EntityQuery<RCDDeconstructableComponent> rcdDeconstructableQuery,
        EntityQuery<ConstructionComponent> constructionQuery,
        EntityQuery<AnchorableComponent> anchorableQuery,
        EntityQuery<DoorComponent> doorQuery
    )
    {
        if (!airtightQuery.HasComponent(uid) ||
            !damageableQuery.TryGetComponent(uid, out var damageableComponent))
            return;

        RemComp(uid, damageableComponent);

        if (constructionQuery.TryGetComponent(uid, out var constructionComponent))
            RemComp(uid, constructionComponent);

        if (rcdDeconstructableQuery.TryGetComponent(uid, out var rcdDeconstructableComponent))
            RemComp(uid, rcdDeconstructableComponent);

        if (anchorableQuery.TryGetComponent(uid, out var anchorableComponent))
            RemComp(uid, anchorableComponent);

        if (doorQuery.HasComponent(uid) &&
            TryComp<WiresPanelComponent>(uid, out var wirePanelComponent))
            RemComp(uid, wirePanelComponent);
    }
}
