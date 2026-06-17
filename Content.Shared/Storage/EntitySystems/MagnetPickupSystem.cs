using Content.Shared.Inventory;
using Content.Shared.Storage.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Network;
//WD toggle shit start - ks14
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Examine;
//wd toggle shit end

namespace Content.Shared.Storage.EntitySystems;

/// <summary>
/// <see cref="MagnetPickupComponent"/>
/// </summary>
public sealed partial class MagnetPickupSystem : EntitySystem // KS14
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedItemSystem _item = default!; //WD ks14 port
    [Dependency] private readonly ItemToggleSystem _itemToggle = default!; //WD ks14 port
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

    [Dependency] private readonly EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(0.5f); // KS14: changed to 0.5

    /// <summary>
    /// Reused list of nearby pickup candidates so we can sort them deterministically without allocating every scan.
    /// </summary>
    private readonly List<EntityUid> _nearby = []; // KS14


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagnetPickupComponent, MapInitEvent>(OnMagnetMapInit);
    }

    private void OnMagnetMapInit(EntityUid uid, MagnetPickupComponent component, MapInitEvent args)
    {
        component.NextScan = _timing.CurTime;
    }

    //WD EDIT start - KS14 port
    private void OnExamined(Entity<MagnetPickupComponent> entity, ref ExaminedEvent args)
    {
        var onMsg = _itemToggle.IsActivated(entity.Owner)
            ? Loc.GetString("comp-magnet-pickup-examined-on")
            : Loc.GetString("comp-magnet-pickup-examined-off");
        args.PushMarkup(onMsg);
    }

    private void OnItemToggled(Entity<MagnetPickupComponent> entity, ref ItemToggledEvent args)
    {
        _item.SetHeldPrefix(entity.Owner, args.Activated ? "on" : "off");
    }
    //WD EDIT end

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        //evil KS14 deprediction hack
        if (!_net.IsServer)
            return;
        //evil KS14 hack end

        var query = EntityQueryEnumerator<MagnetPickupComponent, StorageComponent, TransformComponent, MetaDataComponent>();
        var currentTime = _timing.CurTime;

        while (query.MoveNext(out var uid, out var comp, out var storage, out var xform, out var meta))
        {
            // WD EDIT START - KS14 port
            if (!TryComp<ItemToggleComponent>(uid, out var toggle))
                continue;

            if (!toggle.Activated)
                continue;
            // WD EDIT END

            if (comp.NextScan > currentTime)
                continue;

            comp.NextScan += ScanDelay;
            Dirty(uid, comp);

            if (!_inventory.TryGetContainingSlot((uid, xform, meta), out var slotDef) && comp.SlotIrrespective == false) //KS14
                continue;

            if (slotDef != null && (slotDef.SlotFlags & comp.SlotFlags) == 0x0 && comp.SlotIrrespective == false) //KS14
                continue;

            // No space
            if (!_storage.HasSpace((uid, storage)))
                continue;

            var parentUid = xform.ParentUid;
            var playedSound = false;
            var finalCoords = xform.Coordinates;
            var moverCoords = _transform.GetMoverCoordinates(uid, xform);

            // KS14 START
            _nearby.Clear();
            _nearby.AddRange(_lookup.GetEntitiesInRange(uid, comp.Range, LookupFlags.Dynamic | LookupFlags.Sundries));
            _nearby.Sort((a, b) => GetNetEntity(a).CompareTo(GetNetEntity(b)));

            foreach (var near in _nearby)
            // KS14 END
            {
                if (_whitelistSystem.IsWhitelistFail(storage.Whitelist, near))
                    continue;

                if (!_physicsQuery.TryGetComponent(near, out var physics) || physics.BodyStatus != BodyStatus.OnGround)
                    continue;

                if (near == parentUid)
                    continue;

                // TODO: Probably move this to storage somewhere when it gets cleaned up
                // TODO: This sucks but you need to fix a lot of stuff to make it better
                // the problem is that stack pickups delete the original entity, which is fine, but due to
                // game state handling we can't show a lerp animation for it.
                var nearXform = Transform(near);
                var nearMap = _transform.GetMapCoordinates(near, xform: nearXform);
                var nearCoords = _transform.ToCoordinates(moverCoords.EntityId, nearMap);

                if (!_storage.Insert(uid, near, out var stacked, storageComp: storage, playSound: !playedSound))
                    continue;

                // Play pickup animation for either the stack entity or the original entity.
                if (stacked != null)
                    _storage.PlayPickupAnimation(stacked.Value, nearCoords, finalCoords, nearXform.LocalRotation);
                else
                    _storage.PlayPickupAnimation(near, nearCoords, finalCoords, nearXform.LocalRotation);

                playedSound = true;
            }
        }
    }
}
