using Content.Shared.Body;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Robust.Shared.Containers;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Klovnmed;

/// <summary>
///     Used to regenerate limbs ig.
/// </summary>
public sealed class OrganRegenerationSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

    [Dependency] private readonly EntityQuery<InitialBodyComponent> _initialBodyQuery = default!;
    [Dependency] private readonly EntityQuery<BodyComponent> _bodyQuery = default!;

    private static readonly LocId PopupLocId = "ks-klovnmed-organ-regen-popup";

    public override void Initialize()
    {
        base.Initialize();

        // thievery
        SubscribeLocalEvent<InitialBodyComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnRejuvenate(Entity<InitialBodyComponent> entity, ref RejuvenateEvent args)
    {
        RegenerateForBody(entity!);
    }

    // TODO LCDC: option for whitelist of categories to regen?

    /// <summary>
    ///     Called on a body/organ to regenerate some number of organs and suborgans.
    /// </summary>
    /// <returns>List of organs re-generated.</returns>
    /// <param name="entity">Body/organ to regenerate organs on.</param>
    /// <param name="maxCount">Maximum number of organs to regenerate. Null to regen all possible organs.</param>
    public List<EntityUid>? RegenerateForBody(Entity<InitialBodyComponent?, BodyComponent?> entity, int? maxCount = null)
    {
        if (maxCount == 0)
            return null;

        if (!_initialBodyQuery.Resolve(entity.Owner, ref entity.Comp1) ||
            !_bodyQuery.Resolve(entity.Owner, ref entity.Comp2) ||
            !_containerSystem.TryGetContainer(entity.Owner, BodyHierarchySystem.ConstContainerId, out var container))
            return null;

        var list = new List<EntityUid>();
        foreach (var initialBodyPart in entity.Comp1.Organs)
        {
            AddOrgan((entity.Owner, entity.Comp2), container, initialBodyPart, list, maxCount: ref maxCount);

            if (maxCount == 0)
                break;
        }

        return list;
    }

    private void AddOrgan(in Entity<BodyComponent> bodyEntity, BaseContainer parentOrganContainer, InitialBodyPart datum, List<EntityUid> spawnedUidList, ref int? maxCount)
    {
        if (!bodyEntity.Comp.PresentOrganCategories.ContainsKey(datum.Category))
        {
            var spawnedUid = EntityManager.PredictedSpawn(datum.Entity);
            spawnedUidList.Add(spawnedUid);
            _containerSystem.Insert(spawnedUid, parentOrganContainer);

            // bruh
            if (_netManager.IsServer)
                _popupSystem.PopupEntity(Loc.GetString(PopupLocId, ("name", Name(spawnedUid))), bodyEntity.Owner, bodyEntity.Owner);

            if (maxCount is { } &&
                --maxCount == 0)
                return;
        }

        if (datum.Children is not { } children ||
            !bodyEntity.Comp.PresentOrganCategories.TryGetValue(datum.Category, out var childParentUid) ||
            !_containerSystem.TryGetContainer(childParentUid, BodyHierarchySystem.ConstContainerId, out var childParentOrganContainer))
            return;

        foreach (var childDatum in children)
        {
            AddOrgan(bodyEntity, childParentOrganContainer, childDatum, spawnedUidList, maxCount: ref maxCount);

            if (maxCount == 0)
                return;
        }
    }
}
