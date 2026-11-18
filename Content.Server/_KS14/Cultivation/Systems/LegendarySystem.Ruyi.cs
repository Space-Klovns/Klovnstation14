using System.Numerics;
using Content.Server._KS14.Cultivation.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared.Body;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

public sealed partial class LegendarySystem
{
    [Dependency] private AppearanceSystem _appearanceSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private SharedItemSystem _itemSystem = default!;
    [Dependency] private SharedVisualBodySystem _visualBodySystem = default!;

    private static readonly float RuyiReach = 3f;
    private static readonly float NeedleReach = 1.5f;

    /// <summary>
    ///     "Grow!" and "Shrink!": a needle becomes a mighty golden-banded staff that reaches three tiles, and back.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRuyiUsed(Entity<RuyiStaffComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        SetExtended(entity, !entity.Comp.Extended);
        Shout(args.User, entity.Comp.Extended ? "legendary-ruyi-grow" : "legendary-ruyi-shrink");
        _visualsSystem.Wave(args.User, FxColour.Gold);
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtGoldMotes, args.User);
        _noticeSystem.Announce(args.User, Loc.GetString(entity.Comp.Extended ? "legendary-ruyi-grows" : "legendary-ruyi-shrinks", ("user", args.User)),
            type: PopupType.Small);
    }

    public void SetExtended(Entity<RuyiStaffComponent> entity, bool extended)
    {
        entity.Comp.Extended = extended;
        _appearanceSystem.SetData(entity, RuyiVisuals.Extended, extended);
        _itemSystem.SetSize(entity, extended ? "Huge" : "Tiny");
        if (TryComp<MeleeWeaponComponent>(entity, out var meleeComponent))
        {
            meleeComponent.Range = extended ? RuyiReach : NeedleReach;
            meleeComponent.Damage = CultivationSystem.Damage("Blunt", extended ? 25f : 1f);
            Dirty(entity.Owner, meleeComponent);
        }
    }

    /// <summary>
    ///     Set down at full size it stands like a pillar: thirteen thousand jin that only its master can lift.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRuyiDropped(Entity<RuyiStaffComponent> entity, ref GotUnequippedHandEvent args)
    {
        if (!entity.Comp.Extended)
            return;

        // It stands up once it lands (see UpdateRuyiSlams).
        entity.Comp.Planter = args.User;
        entity.Comp.PlantPending = true;
    }

    [SubscribeLocalEvent]
    private void OnRuyiInteractHand(Entity<RuyiStaffComponent> entity, ref InteractHandEvent args)
    {
        if (args.Handled || entity.Comp.Planter is not { } planterUid || !Transform(entity).Anchored)
            return;

        args.Handled = true;
        if (args.User != planterUid)
        {
            _noticeSystem.Notify(args.User, Loc.GetString("legendary-ruyi-wont-budge"), PopupType.SmallCaution);
            return;
        }

        entity.Comp.Planter = null;
        _transformSystem.Unanchor(entity);
        _handsSystem.PickupOrDrop(args.User, entity);
    }

    /// <summary>
    ///     Thirteen Thousand Jin: the staff shoots up to the heavens and comes crashing down along a three-wide path.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRuyiSlam(Entity<RuyiStaffComponent> entity, ref RuyiSlamActionEvent args)
    {
        if (args.Handled)
            return;

        if (!entity.Comp.Extended)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-ruyi-needle"));
            return;
        }

        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextSlam)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-ruyi-heavy"));
            return;
        }

        var from = _transformSystem.GetMapCoordinates(args.Performer);
        var target = _transformSystem.ToMapCoordinates(args.Target);
        var offset = target.Position - from.Position;
        var distance = MathF.Min(offset.Length(), 10f);
        if (distance < 2f)
            return;

        args.Handled = true;
        entity.Comp.NextSlam = now + TimeSpan.FromSeconds(25);
        Shout(args.Performer, "legendary-ruyi-shout");
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtGoldMotes, args.Performer);
        LegendaryLine(args.Performer, entity, offset, (int)distance, 1, 25f, 2);
        entity.Comp.SlamLanding = new MapCoordinates(from.Position + Vector2.Normalize(offset) * distance, from.MapId);
        entity.Comp.SlamUser = args.Performer;
        entity.Comp.SlamLandsAt = now + TimeSpan.FromSeconds(distance * 0.05f + 0.2f);
        _visualsSystem.Art(CultivationVisualsSystem.ArtFallingStar, entity.Comp.SlamLanding.Value);
    }

    private void UpdateRuyiSlams(TimeSpan now)
    {
        var query = EntityQueryEnumerator<RuyiStaffComponent>();
        while (query.MoveNext(out var uid, out var ruyiComponent))
        {
            if (ruyiComponent.PlantPending)
                TryPlant((uid, ruyiComponent));

            if (ruyiComponent.SlamLanding is not { } landing || now < ruyiComponent.SlamLandsAt)
                continue;

            ruyiComponent.SlamLanding = null;
            var userUid = ruyiComponent.SlamUser ?? uid;
            _visualsSystem.Play(CultivationVisualsSystem.MountainFallSound, landing);
            _bodyArtSystem.Impact(landing, 2f, strength: 2.5f, crater: 2);
            _bodyArtSystem.Smash(landing, 1.5f, 1000f, wallTier: 2, userUid);
            _mobs.Clear();
            _entityLookupSystem.GetEntitiesInRange(landing, 2f, _mobs);
            foreach (var victim in _mobs)
                LegendaryHit(userUid, victim.Owner, 15f, TimeSpan.FromSeconds(2), uid);
        }
    }

    private void TryPlant(Entity<RuyiStaffComponent> entity)
    {
        entity.Comp.PlantPending = false;
        var transformComponent = Transform(entity);
        if (!entity.Comp.Extended || !HasComp<Robust.Shared.Map.Components.MapGridComponent>(transformComponent.ParentUid))
        {
            entity.Comp.Planter = null;
            return;
        }

        _transformSystem.AnchorEntity(entity, transformComponent);
        _visualsSystem.Play(CultivationVisualsSystem.MountainFallSound, entity, -6f);
        _noticeSystem.Announce(entity, Loc.GetString("legendary-ruyi-planted", ("item", entity.Owner)), type: PopupType.Small);
    }

    /// <summary>
    ///     Pluck a few hairs and blow on them: two copies of the wielder leap out and fight someone for ten seconds.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRuyiPluckHairs(Entity<RuyiStaffComponent> entity, ref RuyiPluckHairsActionEvent args)
    {
        if (args.Handled || args.Target == args.Performer)
            return;

        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextHairs)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-ruyi-hairs-regrow"));
            return;
        }

        args.Handled = true;
        entity.Comp.NextHairs = now + TimeSpan.FromSeconds(45);
        Shout(args.Performer, "legendary-ruyi-hairs-shout");
        _noticeSystem.Announce(args.Performer, Loc.GetString("legendary-ruyi-hairs", ("user", args.Performer)), type: PopupType.MediumCaution);

        var from = _transformSystem.GetMapCoordinates(args.Performer);
        for (var i = 0; i < 2; i++)
        {
            var spot = new MapCoordinates(from.Position + _robustRandom.NextVector2(0.8f, 1.2f), from.MapId);
            var cloneUid = Spawn(entity.Comp.Clone, spot);
            _visualBodySystem.CopyAppearanceFrom(args.Performer, cloneUid);
            DressLike(cloneUid, args.Performer);
            _metaDataSystem.SetEntityName(cloneUid, Name(args.Performer));
            _visualsSystem.Art(CultivationVisualsSystem.ArtMistSteam, spot);

            _npcFactionSystem.ClearFactions(cloneUid);
            if (TryComp<NpcFactionMemberComponent>(args.Performer, out var masterFactions) && masterFactions.Factions.Count > 0)
                _npcFactionSystem.AddFactions(cloneUid, new HashSet<ProtoId<NpcFactionPrototype>>(masterFactions.Factions));

            // They share the wielder's factions, so ordinary crew count as friends and plain retaliation would refuse
            //     them: mark the target hostile outright. The clones still never turn on their master.
            _npcFactionSystem.AggroEntity(cloneUid, args.Target);
            EnsureComp<SageCloneComponent>(cloneUid).Master = args.Performer;
        }
    }

    private static readonly string[] CloneSlots = { "jumpsuit", "outerClothing", "head", "shoes", "gloves", "mask", "eyes", "neck" };

    /// <summary>
    ///     A clone wears copies of whatever its master has on. The copies can't be taken off, and they vanish with it.
    /// </summary>
    private void DressLike(EntityUid cloneUid, EntityUid masterUid)
    {
        foreach (var slot in CloneSlots)
        {
            if (!_inventorySystem.TryGetSlotEntity(masterUid, slot, out var worn)
                || MetaData(worn.Value).EntityPrototype?.ID is not { } prototype)
                continue;

            if (_inventorySystem.TryGetSlotEntity(cloneUid, slot, out var existing))
                QueueDel(existing.Value);

            var copyUid = Spawn(prototype, Transform(cloneUid).Coordinates);
            EnsureComp<Content.Shared.Interaction.Components.UnremoveableComponent>(copyUid);
            if (!_inventorySystem.TryEquip(cloneUid, copyUid, slot, silent: true, force: true))
                QueueDel(copyUid);
        }
    }
}
