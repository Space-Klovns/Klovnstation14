using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.Body;
using Content.Shared.Changeling.Components;
using Content.Shared.Cloning;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Server._KS14.Anchorless.Systems;
// KS14 preface - yes this is identical to the ling system with some minor adjustments.
// I made this into a separate system since wizden ling is bound to change and I want to create less work for apstrimi.

public sealed partial class AnchorlessIdentitySystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedCloningSystem _cloning = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedVisualBodySystem _visualBody = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IdentitySystem _identity = default!;

    private MapId? _pausedMapId;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnchorlessIdentityComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AnchorlessIdentityComponent, AnchorlessCommunionEvent>(OnCommunion);
        SubscribeLocalEvent<AnchorlessIdentityComponent, AnchorlessTransformActionEvent>(OnTransformAction);
    }

    private void OnMapInit(Entity<AnchorlessIdentityComponent> ent, ref MapInitEvent args)
    {
        var identity = AddIdentity(ent.Owner, ent.Owner);
        if (identity == null)
            return;

        ent.Comp.LearnedIdentities.Add(identity);
        ent.Comp.CurrentIdentityIndex = 0;
        Dirty(ent);
    }

    private void OnCommunion(Entity<AnchorlessIdentityComponent> ent, ref AnchorlessCommunionEvent args)
    {
        if (!TryGetEntity(args.Other, out var otherUid) || otherUid is not { } resolvedOtherUid)
            return;

        if (!TryComp<AnchorlessIdentityComponent>(resolvedOtherUid, out var otherComp))
            return;

        MergeIdentities(ent, (resolvedOtherUid, otherComp));
    }

    private void OnTransformAction(Entity<AnchorlessIdentityComponent> ent, ref AnchorlessTransformActionEvent args)
    {
        if (ent.Comp.LearnedIdentities.Count == 0)
            return;

        var nextIndex = (ent.Comp.CurrentIdentityIndex + 1) % ent.Comp.LearnedIdentities.Count;
        ent.Comp.CurrentIdentityIndex = nextIndex;

        var targetIdentity = ent.Comp.LearnedIdentities[nextIndex];
        if (targetIdentity.StoredIdentity == null)
        {
            EntityUid? originalEntity = null;
            if (targetIdentity.OriginalEntity is { } originalNetEntity && TryGetEntity(originalNetEntity, out var resolvedOriginal))
                originalEntity = resolvedOriginal;

            var cloned = AddIdentity(ent.Owner, originalEntity ?? ent.Owner);
            if (cloned == null)
                return;

            targetIdentity.StoredIdentity = cloned.StoredIdentity;
            targetIdentity.OriginalEntity = cloned.OriginalEntity;
            targetIdentity.OriginalName = cloned.OriginalName;
            targetIdentity.Starting = cloned.Starting;
        }

        EntityUid? storedIdentity = null;
        if (targetIdentity.StoredIdentity is { } storedNetIdentity && TryGetEntity(storedNetIdentity, out var resolvedStoredIdentity))
            storedIdentity = resolvedStoredIdentity;

        if (storedIdentity is not { } resolvedIdentity)
            return;

        _visualBody.CopyAppearanceFrom(resolvedIdentity, ent.Owner);
        _cloning.CloneComponents(resolvedIdentity, ent.Owner, "ChangelingCloningSettings");
        _metaData.SetEntityName(ent.Owner, targetIdentity.OriginalName, raiseEvents: false);
        _identity.QueueIdentityUpdate(ent.Owner);

        _popup.PopupClient(Loc.GetString("anchorless-transform-message", ("identity", targetIdentity.OriginalName)), ent.Owner, PopupType.Medium);
        Dirty(ent);
    }

    public void MergeIdentities(Entity<AnchorlessIdentityComponent> first, Entity<AnchorlessIdentityComponent> second)
    {
        var merged = AnchorlessIdentityHelper.MergeIdentityData(first.Comp.LearnedIdentities, second.Comp.LearnedIdentities);
        first.Comp.LearnedIdentities = merged;
        second.Comp.LearnedIdentities = merged;

        Dirty(first);
        Dirty(second);
    }

    public AnchorlessIdentityData? AddIdentity(EntityUid owner, EntityUid original)
    {
        EnsurePausedMap();
        if (_pausedMapId == null)
            return null;

        var mapCoords = new MapCoordinates(0, 0, _pausedMapId.Value);
        if (!_cloning.TryCloning(original, mapCoords, "ChangelingCloningSettings", out var clone))
            return null;

        var data = new AnchorlessIdentityData
        {
            StoredIdentity = clone is { } cloneUid ? GetNetEntity(cloneUid) : null,
            OriginalEntity = GetNetEntity(original),
            OriginalName = Name(original),
            Starting = original == owner,
        };

        return data;
    }

    private void EnsurePausedMap()
    {
        if (_pausedMapId != null && _map.MapExists(_pausedMapId))
            return;

        var mapUid = _map.CreateMap(out var newMapId);
        _metaData.SetEntityName(mapUid, Loc.GetString("changeling-paused-map-name"));
        _map.SetPaused(mapUid, true);
        _pausedMapId = newMapId;
    }
}

[Serializable, NetSerializable]
public sealed class AnchorlessCommunionEvent : EntityEventArgs
{
    public NetEntity Other;
}
