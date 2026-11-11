using Content.Shared._KS14.Lava;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Atmos;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Burning;

public sealed partial class KsItemBurningSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedChatSystem _chatSystem = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedItemSystem _itemSystem = default!;
    [Dependency] private EntityQuery<ItemComponent> _itemQuery = default!;

    private static readonly ProtoId<EmotePrototype> ScreamEmote = "Scream";

    /// <summary>
    ///     Items at least this big leave <see cref="KsItemBurningComponent.AshLarge"/>. tgstation's <c>WEIGHT_CLASS_HUGE</c>.
    /// </summary>
    private static readonly ProtoId<ItemSizePrototype> LargeAshSize = "Huge";

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Burning is decided by the server alone; the client only shows the flames.
        if (_netManager.IsClient)
            return;

        var curTime = _gameTiming.CurTime;
        var burningEnumerator = EntityQueryEnumerator<KsItemBurningComponent>();
        while (burningEnumerator.MoveNext(out var uid, out var burningComponent))
        {
            if (curTime < burningComponent.NextTick)
                continue;

            burningComponent.NextTick = curTime + burningComponent.TickInterval;
            Burn((uid, burningComponent), burningComponent.Damage);
        }
    }

    /// <summary>
    ///     Sets an item on fire, or changes it if it has <see cref="KsTransformOnBurnComponent"/>. Lava-proof items and
    ///         items already burning are left alone. Server only.
    /// </summary>
    /// <param name="initialDamage">Dealt at once, as tgstation's <c>fire_act</c> does before the item starts burning.</param>
    public void Ignite(EntityUid uid, DamageSpecifier? initialDamage = null)
    {
        if (_netManager.IsClient ||
            TerminatingOrDeleted(uid) ||
            EntityManager.IsQueuedForDeletion(uid) ||
            HasComp<KsLavaProofComponent>(uid) ||
            HasComp<KsItemBurningComponent>(uid))
            return;

        if (TryComp<KsTransformOnBurnComponent>(uid, out var transformComponent))
        {
            TransformItem((uid, transformComponent));
            return;
        }

        var burningComponent = AddComp<KsItemBurningComponent>(uid);
        burningComponent.NextTick = _gameTiming.CurTime + burningComponent.TickInterval;
        Dirty(uid, burningComponent);

        if (initialDamage != null)
            Burn((uid, burningComponent), initialDamage);
    }

    public void Extinguish(Entity<KsItemBurningComponent?> entity)
    {
        if (!Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return;

        RemComp(entity.Owner, entity.Comp);
    }

    private void Burn(Entity<KsItemBurningComponent> entity, DamageSpecifier damage)
    {
        _damageableSystem.TryChangeDamage(entity.Owner, damage);

        // Destructible may have broken it already.
        if (TerminatingOrDeleted(entity.Owner))
            return;

        entity.Comp.Integrity -= (float)damage.GetTotal();
        if (entity.Comp.Integrity > 0f)
            return;

        var ash = entity.Comp.Ash;
        if (_itemQuery.TryComp(entity.Owner, out var itemComponent) &&
            _itemSystem.GetSizePrototype(itemComponent.Size) >= _itemSystem.GetSizePrototype(LargeAshSize))
            ash = entity.Comp.AshLarge;

        SpawnNextToOrDrop(ash, entity.Owner);
        QueueDel(entity.Owner);
    }

    private void TransformItem(Entity<KsTransformOnBurnComponent> entity)
    {
        var coordinates = Transform(entity.Owner).Coordinates;
        _audioSystem.PlayPvs(entity.Comp.Sound, coordinates);

        // Queued, not deleted: this runs inside step trigger and atmos callbacks that still hold the entity.
        if (_containerSystem.TryGetContainingContainer(entity.Owner, out var container))
        {
            _containerSystem.Remove(entity.Owner, container);
            QueueDel(entity.Owner);
            SpawnInContainerOrDrop(entity.Comp.Prototype, container.Owner, container.ID);
            return;
        }

        QueueDel(entity.Owner);
        SpawnAtPosition(entity.Comp.Prototype, coordinates);
    }

    [SubscribeLocalEvent]
    private void OnTransformTileFire(Entity<KsTransformOnBurnComponent> entity, ref TileFireEvent args)
    {
        Ignite(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnTransformIgnited(Entity<KsTransformOnBurnComponent> entity, ref IgnitedEvent args)
    {
        Ignite(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnExtinguish(Entity<KsItemBurningComponent> entity, ref ExtinguishEvent args)
    {
        Extinguish(entity.AsNullable());
    }

    /// <summary>
    ///     tgstation's burning <c>attack_hand</c>: a bare hand gets burned and lets go, a protected one smothers the
    ///         flames.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnInteractHand(Entity<KsItemBurningComponent> entity, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (_inventorySystem.TryGetInventoryEntity<DamageOnInteractProtectionComponent>(args.User, out _) ||
            HasComp<DamageOnInteractProtectionComponent>(args.User))
        {
            Extinguish(entity.AsNullable());
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-burning-smother", ("item", entity.Owner)), entity.Owner, args.User);
            return;
        }

        args.Handled = true;
        _damageableSystem.TryChangeDamage(args.User, entity.Comp.TouchDamage, origin: entity.Owner);
        _audioSystem.PlayPredicted(entity.Comp.TouchSound, entity.Owner, args.User);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-burning-touch", ("item", entity.Owner)), entity.Owner, args.User, PopupType.SmallCaution);

        if (_netManager.IsServer)
            _chatSystem.TryEmoteWithChat(args.User, ScreamEmote.Id);
    }
}
