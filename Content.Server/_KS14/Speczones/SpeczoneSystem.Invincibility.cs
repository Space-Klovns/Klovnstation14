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
        var loadAsSavedQuery = GetEntityQuery<SpeczoneLoadAsSavedComponent>();
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
                    loadAsSavedQuery,
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
        EntityQuery<SpeczoneLoadAsSavedComponent> loadAsSavedQuery,
        EntityQuery<AirtightComponent> airtightQuery,
        EntityQuery<DamageableComponent> damageableQuery,
        EntityQuery<RCDDeconstructableComponent> rcdDeconstructableQuery,
        EntityQuery<ConstructionComponent> constructionQuery,
        EntityQuery<AnchorableComponent> anchorableQuery,
        EntityQuery<DoorComponent> doorQuery)
    {
        ProcessEntityInvincibility(
            parentUid,
            loadAsSavedQuery,
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
                loadAsSavedQuery,
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
        EntityQuery<SpeczoneLoadAsSavedComponent> loadAsSavedQuery,
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

        if (loadAsSavedQuery.HasComponent(uid))
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
