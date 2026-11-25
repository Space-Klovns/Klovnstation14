using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Content.Server._KS14.Cultivation.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.Flash;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.Chat;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

public sealed partial class LegendarySystem
{
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    /// <summary>
    ///     What counts as answering to your name: "yes", "what", "here", "who's asking"...
    /// </summary>
    private static readonly Regex AnswerPattern = new(@"\b(yes|yeah|yep|what|here|huh|who|me|aye|present|speaking)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private FlammableSystem _flammableSystem = default!;
    [Dependency] private FlashSystem _flashSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedStorageSystem _storageSystem = default!;
    [Dependency] private TagSystem _tagSystem = default!;

    #region Purple-Gold Gourd

    /// <summary>
    ///     Point the gourd and call someone's name. If they answer within a few seconds, they're inside.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGourdCall(Entity<PurpleGoldGourdComponent> entity, ref GourdCallActionEvent args)
    {
        if (args.Handled || args.Target == args.Performer || !HasComp<MobStateComponent>(args.Target))
            return;

        if (entity.Comp.Prisoner != null)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-gourd-full"));
            return;
        }

        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextCall)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-gourd-gathering"));
            return;
        }

        args.Handled = true;
        entity.Comp.NextCall = now + entity.Comp.CallCooldown;
        _chatSystem.TrySendInGameICMessage(args.Performer, Name(args.Target).ToUpperInvariant() + "!", InGameICChatType.Speak,
            hideChat: false, ignoreActionBlocker: true);
        var calledComponent = EnsureComp<GourdCalledComponent>(args.Target);
        calledComponent.Gourd = entity;
        calledComponent.ListenUntil = now + TimeSpan.FromSeconds(6);
        _noticeSystem.Notify(args.Target, Loc.GetString("legendary-gourd-urge", ("user", args.Performer)), PopupType.LargeCaution);
    }

    /// <summary>
    ///     They spoke. Did they answer?
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCalledSpoke(Entity<GourdCalledComponent> entity, ref EntitySpokeEvent args)
    {
        if (_gameTiming.CurTime > entity.Comp.ListenUntil)
        {
            RemCompDeferred<GourdCalledComponent>(entity);
            return;
        }

        if (!IsAnswer(args.Message, entity))
            return;

        var gourdUid = entity.Comp.Gourd;
        RemCompDeferred<GourdCalledComponent>(entity);
        if (TerminatingOrDeleted(gourdUid) || !TryComp<PurpleGoldGourdComponent>(gourdUid, out var gourdComponent) || gourdComponent.Prisoner != null
            || !_transformSystem.InRange((gourdUid, null), (entity.Owner, null), 11f))
            return;

        // Being pulled into a container in the middle of speech is no work for now: it waits for Update.
        gourdComponent.Prisoner = entity;
        gourdComponent.ReleaseAt = TimeSpan.Zero;
    }

    /// <summary>
    ///     A question back, a "yes", "what", "here", or their own name.
    /// </summary>
    public bool IsAnswer(string message, EntityUid victimUid)
    {
        if (message.Contains('?') || AnswerPattern.IsMatch(message))
            return true;

        var firstName = Name(victimUid).Split(' ').FirstOrDefault();
        return !string.IsNullOrEmpty(firstName) && message.Contains(firstName, StringComparison.OrdinalIgnoreCase);
    }

    public void SuckIn(Entity<PurpleGoldGourdComponent> entity, EntityUid victimUid)
    {
        var container = _containerSystem.EnsureContainer<ContainerSlot>(entity, PurpleGoldGourdComponent.ContainerId);
        _visualsSystem.Art(CultivationVisualsSystem.ArtVoidMotes, victimUid);
        _noticeSystem.Announce(victimUid, Loc.GetString("legendary-gourd-sucked", ("target", victimUid)), type: PopupType.LargeCaution);
        if (!_containerSystem.Insert(victimUid, container, force: true))
        {
            entity.Comp.Prisoner = null;
            return;
        }

        var now = _gameTiming.CurTime;
        entity.Comp.Prisoner = victimUid;
        entity.Comp.ReleaseAt = now + entity.Comp.HoldTime;
        entity.Comp.NextDissolve = now + TimeSpan.FromSeconds(1);
        _bodyCultivationSystem.BreakHardening(victimUid);
        _noticeSystem.Notify(victimUid, Loc.GetString("legendary-gourd-inside"), PopupType.Large);
    }

    private void UpdateGourds(TimeSpan now)
    {
        var query = EntityQueryEnumerator<PurpleGoldGourdComponent>();
        while (query.MoveNext(out var uid, out var gourdComponent))
        {
            if (gourdComponent.Prisoner is not { } prisonerUid)
                continue;

            if (gourdComponent.ReleaseAt == TimeSpan.Zero)
            {
                SuckIn((uid, gourdComponent), prisonerUid);
                continue;
            }

            if (TerminatingOrDeleted(prisonerUid) || !_containerSystem.TryGetContainingContainer(prisonerUid, out var container) || container.Owner != uid)
            {
                gourdComponent.Prisoner = null;
                continue;
            }

            if (_mobStateSystem.IsDead(prisonerUid))
            {
                Distill((uid, gourdComponent), prisonerUid);
                continue;
            }

            if (now >= gourdComponent.ReleaseAt)
            {
                Release((uid, gourdComponent));
                continue;
            }

            if (now < gourdComponent.NextDissolve)
                continue;

            // The gourd's wine slowly dissolves whoever is inside, and a body cultivator's training with them.
            gourdComponent.NextDissolve = now + TimeSpan.FromSeconds(1);
            _damageableSystem.TryChangeDamage(prisonerUid, CultivationSystem.Damage("Caustic", gourdComponent.DissolveDamage), ignoreResistances: true,
                interruptsDoAfters: false);
            if (TryComp<BodyCultivatorComponent>(prisonerUid, out var bodyComponent))
            {
                _bodyCultivationSystem.AddTempering((prisonerUid, bodyComponent), -2f);
                _bodyCultivationSystem.AddExhaustion((prisonerUid, bodyComponent), 10f);
            }
        }
    }

    public void Release(Entity<PurpleGoldGourdComponent> entity)
    {
        entity.Comp.Prisoner = null;
        entity.Comp.Escaping = false;
        if (!_containerSystem.TryGetContainer(entity, PurpleGoldGourdComponent.ContainerId, out var container) || container.ContainedEntities.Count == 0)
            return;

        _containerSystem.EmptyContainer(container, force: true);
        _visualsSystem.Art(CultivationVisualsSystem.ArtMistSteam, entity);
        _noticeSystem.Announce(entity, Loc.GetString("legendary-gourd-released", ("item", entity.Owner)), type: PopupType.Medium);
    }

    /// <summary>
    ///     The wine has finished its work: the remains tumble out, and so does an elixir.
    /// </summary>
    private void Distill(Entity<PurpleGoldGourdComponent> entity, EntityUid dissolvedUid)
    {
        var elixirUid = Spawn(entity.Comp.Elixir, Transform(entity).Coordinates);
        EnsureComp<GourdElixirComponent>(elixirUid).DistilledFrom = Name(dissolvedUid);
        Release(entity);
        _noticeSystem.Announce(entity, Loc.GetString("legendary-gourd-distilled", ("item", entity.Owner)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnGourdUsed(Entity<PurpleGoldGourdComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled || entity.Comp.Prisoner == null)
            return;

        args.Handled = true;
        Release(entity);
    }

    /// <summary>
    ///     Squirming against the cork takes a long while.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGourdRelayMovement(Entity<PurpleGoldGourdComponent> entity, ref ContainerRelayMovementEntityEvent args)
    {
        if (entity.Comp.Escaping || args.Entity != entity.Comp.Prisoner)
            return;

        entity.Comp.Escaping = true;
        _noticeSystem.Notify(args.Entity, Loc.GetString("legendary-gourd-squirm"));
        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.Entity, entity.Comp.EscapeTime, new GourdEscapeDoAfterEvent(), entity, target: entity)
        {
            BreakOnMove = false,
            NeedHand = false,
        });
    }

    [SubscribeLocalEvent]
    private void OnGourdEscape(Entity<PurpleGoldGourdComponent> entity, ref GourdEscapeDoAfterEvent args)
    {
        entity.Comp.Escaping = false;
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        Release(entity);
    }

    [SubscribeLocalEvent]
    private void OnGourdExamined(Entity<PurpleGoldGourdComponent> entity, ref ExaminedEvent args)
    {
        if (entity.Comp.Prisoner != null)
            args.PushMarkup(Loc.GetString("legendary-gourd-occupied"));
    }

    /// <summary>
    ///     Stolen essence. Grim, and extraordinarily potent.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnElixirUsed(Entity<GourdElixirComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var eaterUid = args.User;
        if (TryComp<CultivatorComponent>(eaterUid, out var cultivatorComponent))
        {
            _sharedCultivationSystem.AddInsight((eaterUid, cultivatorComponent), 50f);
            _sharedCultivationSystem.AddQi((eaterUid, cultivatorComponent), _sharedCultivationSystem.GetMaxQi(cultivatorComponent));
            _visualsSystem.Wave(eaterUid, FxColour.Gold);
            _noticeSystem.Notify(eaterUid, Loc.GetString("legendary-elixir-cultivator"), PopupType.Large);
        }
        else if (TryComp<BodyCultivatorComponent>(eaterUid, out var bodyComponent) && bodyComponent.Committed)
        {
            _bodyCultivationSystem.AddTempering((eaterUid, bodyComponent), 100f);
            _damageableSystem.HealEvenly(eaterUid, FixedPoint2.New(-60));
            _noticeSystem.Notify(eaterUid, Loc.GetString("legendary-elixir-body"), PopupType.Large);
        }
        else
        {
            _damageableSystem.HealEvenly(eaterUid, FixedPoint2.New(-80));
            _noticeSystem.Notify(eaterUid, Loc.GetString("legendary-elixir-mortal"), PopupType.Medium);
        }

        QueueDel(entity);
    }

    [SubscribeLocalEvent]
    private void OnElixirExamined(Entity<GourdElixirComponent> entity, ref ExaminedEvent args)
    {
        if (entity.Comp.DistilledFrom.Length > 0)
            args.PushMarkup(Loc.GetString("legendary-elixir-face", ("name", entity.Comp.DistilledFrom)));
    }

    #endregion

    #region Plantain Fan

    [SubscribeLocalEvent]
    private void OnFanGust(Entity<PlantainFanComponent> entity, ref PlantainGustActionEvent args)
    {
        if (args.Handled || !TryFan(entity, args.Performer))
            return;

        args.Handled = true;
        var from = _transformSystem.GetMapCoordinates(args.Performer);
        var direction = _transformSystem.ToMapCoordinates(args.Target).Position - from.Position;
        if (direction.LengthSquared() < 0.01f)
            direction = new Vector2(0f, 1f);

        direction = Vector2.Normalize(direction);
        Shout(args.Performer, "legendary-fan-hurricane");
        _visualsSystem.Distort(from, FxDistortion.Push);
        Gale(args.Performer, entity, from, 9f, direction);
    }

    /// <summary>
    ///     Whirled overhead: a typhoon all around.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnFanUsed(Entity<PlantainFanComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled || !TryFan(entity, args.User))
            return;

        args.Handled = true;
        Shout(args.User, "legendary-fan-typhoon");
        var from = _transformSystem.GetMapCoordinates(args.User);
        _visualsSystem.Distort(from, FxDistortion.Roar);
        Gale(args.User, entity, from, 5f, null);
    }

    private bool TryFan(Entity<PlantainFanComponent> entity, EntityUid userUid)
    {
        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextGust)
        {
            _noticeSystem.Notify(userUid, Loc.GetString("legendary-fan-resting"));
            return false;
        }

        entity.Comp.NextGust = now + entity.Comp.GustCooldown;
        _audioSystem.PlayPvs(RepulseSound, userUid);
        _visualsSystem.Art(CultivationVisualsSystem.ArtMistDust, userUid);
        return true;
    }

    /// <summary>
    ///     Everything loose in the gale goes flying, fires gutter out, and glass shatters. With no direction it blows
    ///         outward all around.
    /// </summary>
    private void Gale(EntityUid userUid, EntityUid fanUid, MapCoordinates from, float range, Vector2? direction)
    {
        foreach (var uid in _entityLookupSystem.GetEntitiesInRange(from, range, LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries))
        {
            if (uid == userUid || uid == fanUid || _containerSystem.IsEntityInContainer(uid))
                continue;

            var offset = _transformSystem.GetMapCoordinates(uid).Position - from.Position;
            if (offset.LengthSquared() < 0.01f)
                continue;

            var away = Vector2.Normalize(offset);
            if (direction is { } blow)
            {
                if (Vector2.Dot(away, blow) < 0.7f)
                    continue;

                away = blow;
            }

            if (_tagSystem.HasTag(uid, WindowTag))
            {
                _damageableSystem.TryChangeDamage(uid, CultivationSystem.Damage("Structural", 50f), origin: userUid);
                continue;
            }

            if (Transform(uid).Anchored)
                continue;

            if (HasComp<MobStateComponent>(uid))
            {
                if (TryComp<FlammableComponent>(uid, out var flammableComponent))
                    _flammableSystem.Extinguish(uid, flammableComponent);

                LegendaryHit(userUid, uid, 5f, TimeSpan.FromSeconds(2), fanUid);
            }

            _throwingSystem.TryThrow(uid, away * 10f, baseThrowSpeed: 15f, user: userUid, recoil: false);
        }
    }

    /// <summary>
    ///     Fanned the other way, a fire becomes a firestorm racing away from you.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnFanFirestorm(Entity<PlantainFanComponent> entity, ref PlantainFirestormActionEvent args)
    {
        if (args.Handled)
            return;

        var from = _transformSystem.GetMapCoordinates(args.Performer);
        var direction = _transformSystem.ToMapCoordinates(args.Target).Position - from.Position;
        if (direction.LengthSquared() < 0.01f || _transformSystem.GetGrid(args.Performer) is not { } gridUid
            || !TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return;

        direction = Vector2.Normalize(direction);
        var burning = new List<Vector2>();
        for (var x = -9; x <= 9; x++)
        {
            for (var y = -9; y <= 9; y++)
            {
                var spot = from.Position + new Vector2(x, y);
                var offset = spot - from.Position;
                if (offset.LengthSquared() < 1f || Vector2.Dot(Vector2.Normalize(offset), direction) < 0.7f)
                    continue;

                var tile = _mapSystem.TileIndicesFor(gridUid, gridComponent, new MapCoordinates(spot, from.MapId));
                if (_atmosphereSystem.IsHotspotActive(gridUid, tile))
                    burning.Add(spot);
            }
        }

        if (burning.Count == 0)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-fan-no-fire"));
            return;
        }

        if (!TryFan(entity, args.Performer))
            return;

        args.Handled = true;
        Shout(args.Performer, "legendary-fan-firestorm");
        foreach (var spot in burning)
        {
            for (var step = 1; step <= 4; step++)
            {
                var point = new MapCoordinates(spot + direction * step, from.MapId);
                var tile = _mapSystem.TileIndicesFor(gridUid, gridComponent, point);
                _atmosphereSystem.HotspotExpose(gridUid, tile, 1000f, 100f, args.Performer, soh: true);
                _visualsSystem.Flames(point, 0.3f, 1);
            }
        }
    }

    #endregion

    #region Bagua Mirror

    /// <summary>
    ///     The Eight Trigrams Seal: every cultivator who sees the light has their cultivation sealed, the wicked burn,
    ///         and everyone else is dazzled.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMirrorUsed(Entity<BaguaMirrorComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextFlash)
        {
            _noticeSystem.Notify(args.User, Loc.GetString("legendary-mirror-dim"));
            return;
        }

        entity.Comp.NextFlash = now + entity.Comp.FlashCooldown;
        Shout(args.User, "legendary-mirror-shout");
        _visualsSystem.Play(CultivationVisualsSystem.TempleBellSound, args.User);
        _visualsSystem.Wave(args.User, FxColour.Gold, big: true);
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtRoarGold, args.User);

        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(args.User).Coordinates, 6f, _mobs);
        foreach (var mob in _mobs)
        {
            var victimUid = mob.Owner;
            if (victimUid == args.User || !_examineSystem.InRangeUnOccluded(args.User, victimUid, 6f))
                continue;

            if (HasComp<JiangshiComponent>(victimUid) || HasComp<DemonicPathComponent>(victimUid))
            {
                _damageableSystem.TryChangeDamage(victimUid, CultivationSystem.Damage("Heat", 20f), ignoreResistances: true, origin: args.User);
                _stunSystem.TryAddParalyzeDuration(victimUid, TimeSpan.FromSeconds(3));
                _noticeSystem.Notify(victimUid, Loc.GetString("legendary-mirror-sears"), PopupType.LargeCaution);
            }

            if (HasComp<CultivatorComponent>(victimUid) || HasComp<BodyCultivatorComponent>(victimUid))
            {
                Seal(victimUid, entity.Comp.SealDuration);
                _stunSystem.TryKnockdown(victimUid, TimeSpan.FromSeconds(1));
            }
            else
            {
                _flashSystem.Flash(victimUid, args.User, entity, TimeSpan.FromSeconds(3), 0.8f);
            }
        }
    }

    /// <summary>
    ///     Lock someone's cultivation away: no techniques, no body arts, no Iron Shirt or Golden Body.
    /// </summary>
    public void Seal(EntityUid uid, TimeSpan duration)
    {
        var sealedComponent = EnsureComp<CultivationSealedComponent>(uid);
        sealedComponent.Until = _gameTiming.CurTime + duration;
        Dirty(uid, sealedComponent);
        _bodyCultivationSystem.BreakHardening(uid);
        _visualsSystem.Wave(uid, FxColour.Gold);
        _noticeSystem.Notify(uid, Loc.GetString("legendary-mirror-sealed"), PopupType.LargeCaution);
    }

    #endregion

    #region Qiankun Pouch

    /// <summary>
    ///     Swallow Heaven and Earth: every loose thing within five tiles flies into the pouch.
    ///     Used from the pouch's action, so using the pouch in hand opens it like any bag.
    /// </summary>
    private void PouchSwallow(Entity<QiankunPouchComponent> entity, EntityUid user)
    {
        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextSwallow)
        {
            _noticeSystem.Notify(user, Loc.GetString("legendary-pouch-wind"));
            return;
        }

        entity.Comp.NextSwallow = now + entity.Comp.Cooldown;
        Shout(user, "legendary-pouch-swallow");
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtVoidMotes, user);
        var swallowed = 0;
        foreach (var uid in _entityLookupSystem.GetEntitiesInRange(Transform(user).Coordinates, 5f, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (uid == entity.Owner || !HasComp<ItemComponent>(uid) || Transform(uid).Anchored || _containerSystem.IsEntityInContainer(uid))
                continue;

            if (_storageSystem.Insert(entity, uid, out _, user: user, playSound: false))
                swallowed++;
        }

        _noticeSystem.Notify(user, Loc.GetString("legendary-pouch-swallowed", ("count", swallowed)), PopupType.Medium);
    }

    /// <summary>
    ///     Heaven and Earth, one button: used at your own feet it swallows; used anywhere else, everything inside comes
    ///         flying out at that spot.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPouchRelease(Entity<QiankunPouchComponent> entity, ref QiankunReleaseActionEvent args)
    {
        if (args.Handled)
            return;

        if (_transformSystem.InRange(args.Target, Transform(args.Performer).Coordinates, 1.5f))
        {
            args.Handled = true;
            PouchSwallow(entity, args.Performer);
            return;
        }

        if (!TryComp<StorageComponent>(entity, out var storageComponent))
            return;

        var stored = storageComponent.Container.ContainedEntities.ToList();
        if (stored.Count == 0)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-pouch-empty"));
            return;
        }

        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextRelease)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-pouch-settling"));
            return;
        }

        args.Handled = true;
        entity.Comp.NextRelease = now + entity.Comp.Cooldown;
        Shout(args.Performer, "legendary-pouch-release");
        _visualsSystem.Distort(_transformSystem.GetMapCoordinates(args.Performer), FxDistortion.Push);
        var from = Transform(args.Performer).Coordinates;
        foreach (var itemUid in stored)
        {
            _containerSystem.Remove(itemUid, storageComponent.Container, destination: from);
            _throwingSystem.TryThrow(itemUid, args.Target, baseThrowSpeed: 15f, user: args.Performer, recoil: false);
        }
    }

    /// <summary>
    ///     Someone beside a pouch's holder can step into its little world to hide, and steps back out with a step.
    /// </summary>
    private void OnHideVerbs(GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User == args.Target || !HasComp<MobStateComponent>(args.User))
            return;

        foreach (var heldUid in _handsSystem.EnumerateHeld(args.Target))
        {
            if (!TryComp<QiankunPouchComponent>(heldUid, out var pouchComponent) || pouchComponent.Guest != null)
                continue;

            var guestUid = args.User;
            var pouch = (heldUid, pouchComponent);
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("legendary-pouch-hide-verb"),
                Act = () => Hide(pouch, guestUid),
            });
            return;
        }
    }

    public void Hide(Entity<QiankunPouchComponent> entity, EntityUid guestUid)
    {
        if (entity.Comp.Guest != null)
            return;

        var world = _containerSystem.EnsureContainer<ContainerSlot>(entity, QiankunPouchComponent.WorldContainerId);
        _visualsSystem.Rift(_transformSystem.GetMapCoordinates(guestUid));
        if (!_containerSystem.Insert(guestUid, world, force: true))
            return;

        entity.Comp.Guest = guestUid;
        entity.Comp.GuestUntil = _gameTiming.CurTime + entity.Comp.HideTime;
        _noticeSystem.Notify(guestUid, Loc.GetString("legendary-pouch-inside"), PopupType.Medium);
    }

    public void LetOut(Entity<QiankunPouchComponent> entity)
    {
        entity.Comp.Guest = null;
        if (!_containerSystem.TryGetContainer(entity, QiankunPouchComponent.WorldContainerId, out var world) || world.ContainedEntities.Count == 0)
            return;

        _containerSystem.EmptyContainer(world, force: true);
        _visualsSystem.Rift(_transformSystem.GetMapCoordinates(entity));
    }

    [SubscribeLocalEvent]
    private void OnPouchRelayMovement(Entity<QiankunPouchComponent> entity, ref ContainerRelayMovementEntityEvent args)
    {
        if (args.Entity == entity.Comp.Guest)
            entity.Comp.GuestUntil = _gameTiming.CurTime;
    }

    private void UpdatePouches(TimeSpan now)
    {
        var query = EntityQueryEnumerator<QiankunPouchComponent>();
        while (query.MoveNext(out var uid, out var pouchComponent))
        {
            if (pouchComponent.Guest != null && now >= pouchComponent.GuestUntil)
                LetOut((uid, pouchComponent));
        }
    }

    [SubscribeLocalEvent]
    private void OnPouchVerbs(Entity<QiankunPouchComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || entity.Comp.Guest == null)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("legendary-pouch-let-out-verb"),
            Act = () => LetOut(entity),
        });
    }

    #endregion
}
