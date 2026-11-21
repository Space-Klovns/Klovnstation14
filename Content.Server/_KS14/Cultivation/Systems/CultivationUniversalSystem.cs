using Content.Shared.Physics;
using Content.Shared.Maps;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Speech.Muting;
using Content.Shared.StatusEffect;
using Content.Shared.Throwing;
using Robust.Server.Audio;
using Content.Shared.Coordinates.Helpers;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The techniques every cultivator comes to as they rise, whatever their laws: sensing, moving, pressing down
///         on the weak, sealing acupoints, writing talismans, passing on a law, hiding, forcing the qi, letting go
///         of a law, and inscribing formations.
/// </summary>
public sealed partial class CultivationUniversalSystem : EntitySystem
{
    private static readonly SoundSpecifier PalmSound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg");
    private static readonly SoundSpecifier StepSound = new SoundPathSpecifier("/Audio/Magic/blink.ogg");
    private static readonly SoundSpecifier PressureSound = new SoundPathSpecifier("/Audio/Magic/rumble.ogg");
    private static readonly SoundSpecifier WriteSound = new SoundPathSpecifier("/Audio/Effects/chime.ogg");
    private static readonly EntProtoId TalismanPrototype = "KsQiTalisman";
    private static readonly EntProtoId JadeSlipPrototype = "KsJadeTransmissionSlip";
    private static readonly EntProtoId GatheringArrayPrototype = "KsGatheringArray";
    private static readonly EntProtoId BarrierPrototype = "KsQiBarrier";
    private static readonly EntProtoId AlarmArrayPrototype = "KsAlarmArray";
    private static readonly string MutedStatus = "Muted";

    /// <summary>
    ///     How far an alarm array listens.
    /// </summary>
    private const float AlarmRange = 3f;

    [Dependency] private CultivationLoadoutSystem _loadoutSystem = default!;
    [Dependency] private InheritanceSystem _inheritanceSystem = default!;
    [Dependency] private CultivationSiteSystem _siteSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private CultivationStanceSystem _stanceSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private FlammableSystem _flammableSystem = default!;
    [Dependency] private ForbiddenArtsSystem _forbiddenArtsSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;
    [Dependency] private EntityQuery<BodyCultivatorComponent> _bodyCultivatorQuery = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();
    private readonly HashSet<Entity<CultivationManualComponent>> _manuals = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _gameTiming.CurTime;
        var alarmEnumerator = EntityQueryEnumerator<AlarmArrayComponent>();
        while (alarmEnumerator.MoveNext(out var arrayUid, out var alarmComponent))
        {
            if (currentTime < alarmComponent.NextScan)
                continue;

            alarmComponent.NextScan = currentTime + TimeSpan.FromSeconds(1);
            ScanAlarm((arrayUid, alarmComponent));
        }
    }

    #region Helpers

    /// <summary>
    ///     How far along the path someone is, to compare who presses down on whom: two per qi realm, or body stage.
    /// </summary>
    public int PathPower(EntityUid uid)
    {
        if (_cultivatorQuery.TryComp(uid, out var cultivatorComponent))
            return 1 + 2 * (int)cultivatorComponent.Realm;

        return _bodyCultivatorQuery.TryComp(uid, out var bodyComponent) ? bodyComponent.Stage : 0;
    }

    private List<EntityUid> MobsAround(EntityUid userUid, float range)
    {
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(userUid).Coordinates, range, _mobs);
        var found = new List<EntityUid>();
        foreach (var mobEntity in _mobs)
        {
            if (mobEntity.Owner != userUid)
                found.Add(mobEntity.Owner);
        }

        return found;
    }

    #endregion

    #region Spiritual Sense

    [SubscribeLocalEvent]
    private void OnSpiritualSense(Entity<CultivatorComponent> entity, ref SpiritualSenseActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtRoarSense, entity);
        _visualsSystem.Play(CultivationVisualsSystem.WindChimesSound, entity);
        _visualsSystem.Wave(entity, FxColour.Sense, big: true);
        var found = 0;
        foreach (var otherUid in MobsAround(entity, args.Range))
        {
            if (HasComp<CultivationConcealedComponent>(otherUid))
                continue;

            var sensedEvent = new CultivationSensedEvent(entity);
            RaiseLocalEvent(otherUid, ref sensedEvent);
            if (sensedEvent.Found)
                found++;

            if (_cultivatorQuery.TryComp(otherUid, out var otherCultivator))
            {
                _popupSystem.PopupEntity(Loc.GetString("cultivation-sense-cultivator",
                    ("target", otherUid), ("realm", Loc.GetString($"cultivation-realm-{otherCultivator.Realm}"))), otherUid, entity);
                found++;
            }
            else if (_bodyCultivatorQuery.TryComp(otherUid, out var otherBody) && otherBody.Stage > 0)
            {
                _popupSystem.PopupEntity(Loc.GetString("cultivation-sense-body",
                    ("target", otherUid), ("stage", Loc.GetString($"body-stage-{otherBody.Stage}"))), otherUid, entity);
                found++;
            }
        }

        _manuals.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(entity).Coordinates, args.Range, _manuals);
        foreach (var manualEntity in _manuals)
            _popupSystem.PopupEntity(Loc.GetString("cultivation-sense-manual"), manualEntity, entity);

        if (found == 0 && _manuals.Count == 0)
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-sense-nothing"));

        // A full reading of the place: its feng shui, and any wild qi nearby.
        var lines = _siteSystem.Evaluate(entity).Lines;
        lines.AddRange(_siteSystem.StationQiLines(entity));
        _noticeSystem.Notify(entity, string.Join("\n", lines), alsoToChat: true);
    }

    #endregion

    #region Empty Palm, Qinggong, Void Step

    [SubscribeLocalEvent]
    private void OnEmptyPalm(Entity<CultivatorComponent> entity, ref EmptyPalmActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtPalmQi, args.Target);
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));
        var direction = _transformSystem.GetMapCoordinates(args.Target).Position - _transformSystem.GetMapCoordinates(entity).Position;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        _throwingSystem.TryThrow(args.Target, Vector2.Normalize(direction) * args.Distance * MathF.Sqrt(_cultivationSystem.Power(entity)), baseThrowSpeed: 10f, user: entity, recoil: false);
        _staminaSystem.TakeStaminaDamage(args.Target, args.StaminaDamage * _cultivationSystem.Power(entity), source: entity);
        _audioSystem.PlayPvs(PalmSound, args.Target);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-empty-palm", ("user", entity.Owner), ("target", args.Target)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnQinggong(Entity<CultivatorComponent> entity, ref QinggongActionEvent args)
    {
        if (args.Handled)
            return;

        var offset = _transformSystem.ToMapCoordinates(args.Target).Position - _transformSystem.GetMapCoordinates(entity).Position;
        if (offset.LengthSquared() < 0.01f || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.QiCloud, entity);
        _visualsSystem.Motes(entity, FxColour.Qi, TimeSpan.FromSeconds(1));
        _visualsSystem.Play(CultivationVisualsSystem.QinggongSound, entity);
        var maxDistance = args.BaseDistance + 2f * (float)entity.Comp.Realm;
        if (offset.Length() > maxDistance)
            offset = Vector2.Normalize(offset) * maxDistance;

        _throwingSystem.TryThrow(entity, offset, baseThrowSpeed: 14f, recoil: false, doSpin: false);
    }

    [SubscribeLocalEvent]
    private void OnVoidStep(Entity<CultivatorComponent> entity, ref VoidStepActionEvent args)
    {
        if (args.Handled)
            return;

        // The void runs beneath walls and doors: any open floor within reach will do, even past a wall or a door you
        //     have no access to. Only a spot inside something solid, or off the station, is refused.
        var targetMap = _transformSystem.ToMapCoordinates(args.Target);
        var here = _transformSystem.GetMapCoordinates(entity);
        if (targetMap.MapId != here.MapId || (targetMap.Position - here.Position).Length() > args.MaxDistance
            || !_turfSystem.TryGetTileRef(args.Target, out var tile) || _turfSystem.IsSpace(tile.Value)
            || _turfSystem.IsTileBlocked(tile.Value, CollisionGroup.Impassable))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-void-step-blocked"));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _audioSystem.PlayPvs(StepSound, entity);
        _visualsSystem.Rift(_transformSystem.GetMapCoordinates(entity));
        _visualsSystem.Wave(entity, FxColour.Void);
        _transformSystem.SetCoordinates(entity, args.Target);
        _visualsSystem.Rift(_transformSystem.GetMapCoordinates(entity));
        _visualsSystem.Motes(entity, FxColour.Void, TimeSpan.FromSeconds(1));
        _audioSystem.PlayPvs(StepSound, entity);
    }

    #endregion

    #region Realm Pressure, Acupoint Sealing

    [SubscribeLocalEvent]
    private void OnRealmPressure(Entity<CultivatorComponent> entity, ref RealmPressureActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtRoarGold, entity);
        _visualsSystem.Distort(_transformSystem.GetMapCoordinates(entity), FxDistortion.Pulse);
        var power = PathPower(entity);
        foreach (var otherUid in MobsAround(entity, args.Range))
        {
            if (PathPower(otherUid) >= power || _sharedCultivationSystem.IsAlly(entity, otherUid))
                continue;

            _stanceSystem.ApplySnare(otherUid, args.Duration, 0.6f);
            _staminaSystem.TakeStaminaDamage(otherUid, args.StaminaDamage * _cultivationSystem.Power(entity), source: entity);
            _noticeSystem.Notify(otherUid, Loc.GetString("cultivation-realm-pressure-crushed"), type: PopupType.MediumCaution);
        }

        _audioSystem.PlayPvs(PressureSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-realm-pressure", ("user", entity.Owner)), type: PopupType.LargeCaution);
    }

    [SubscribeLocalEvent]
    private void OnAcupointSealing(Entity<CultivatorComponent> entity, ref AcupointSealingActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtFistQi, args.Target);
        _visualsSystem.Strike(entity, args.Target, _visualsSystem.ColourOf(entity));

        // Anything that answers the seal (an Exploding Heart Palm waiting to go off) gets first claim on it.
        var ev = new AcupointSealedEvent(entity);
        RaiseLocalEvent(args.Target, ref ev);
        if (ev.Handled)
            return;

        // The voice and the legs: they can't call out, and can barely walk.
        _statusEffectsSystem.TryAddStatusEffect<MutedComponent>(args.Target, MutedStatus, args.Duration, refresh: true);
        _stanceSystem.ApplySnare(args.Target, args.Duration * 0.6, 0.5f);
        _noticeSystem.Announce(args.Target, Loc.GetString("cultivation-acupoint-sealed", ("user", entity.Owner), ("target", args.Target)), type: PopupType.MediumCaution);
    }

    #endregion

    #region Talismans and transmission

    [SubscribeLocalEvent]
    private void OnWriteTalisman(Entity<CultivatorComponent> entity, ref WriteTalismanActionEvent args)
    {
        if (args.Handled)
            return;

        if (!_handsSystem.TryGetActiveItem(entity.Owner, out var paperUid) || !HasComp<PaperComponent>(paperUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-talisman-needs-paper"));
            return;
        }

        if (_sharedCultivationSystem.GetRootLaw(entity.Comp) is not { } rootLaw || !ProtoMan.TryIndex(rootLaw, out var law))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-talisman-needs-law"));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtTalismanFlare, entity);
        QueueDel(paperUid.Value);
        var talismanUid = Spawn(TalismanPrototype, Transform(entity).Coordinates);
        EnsureComp<QiTalismanComponent>(talismanUid).Element = law.Element;
        _handsSystem.PickupOrDrop(entity, talismanUid, checkActionBlocker: false);
        _audioSystem.PlayPvs(WriteSound, entity);
        _noticeSystem.Notify(entity, Loc.GetString("cultivation-talisman-written", ("element", Loc.GetString($"cultivation-element-{law.Element}"))), type: PopupType.Medium);
    }

    /// <summary>
    ///     A talisman spends the qi written into it, by the writer's Root Law.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnTalismanUsed(Entity<QiTalismanComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var userUid = args.User;
        switch (entity.Comp.Element)
        {
            case CultivationElement.Metal:
                foreach (var otherUid in MobsAround(userUid, 3f))
                {
                    _damageableSystem.TryChangeDamage(otherUid, _cultivationSystem.Empowered(userUid, CultivationSystem.Damage("Slash", 20f)), origin: userUid);
                    break;
                }
                break;
            case CultivationElement.Water:
                _stanceSystem.ApplyWard(userUid, TimeSpan.FromSeconds(10), 0.5f, releaseWave: false);
                break;
            case CultivationElement.Fire:
                foreach (var otherUid in MobsAround(userUid, 3f))
                    _flammableSystem.AdjustFireStacks(otherUid, 2f, ignite: true);
                break;
            case CultivationElement.Wood:
                _damageableSystem.HealEvenly(userUid, FixedPoint2.New(-25));
                break;
            case CultivationElement.Earth:
                _stanceSystem.ApplyRootedStance(userUid, TimeSpan.FromSeconds(10), 0.6f, 0.8f);
                break;
        }

        _noticeSystem.Announce(userUid, Loc.GetString("cultivation-talisman-burns", ("user", userUid)), type: PopupType.Medium);
        QueueDel(entity);
    }

    /// <summary>
    ///     Any talisman pressed to a jiangshi's brow seals it, and the corpse drops.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnTalismanAfterInteract(Entity<QiTalismanComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } targetUid || !_forbiddenArtsSystem.TrySealPuppet(targetUid))
            return;

        args.Handled = true;
        _noticeSystem.Announce(args.User, Loc.GetString("cultivation-talisman-seals-jiangshi", ("user", args.User)), type: PopupType.Medium);
        QueueDel(entity);
    }

    [SubscribeLocalEvent]
    private void OnTransmitLaw(Entity<CultivatorComponent> entity, ref TransmitLawActionEvent args)
    {
        if (args.Handled)
            return;

        if (_sharedCultivationSystem.GetRootLaw(entity.Comp) is not { } rootLaw)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-talisman-needs-law"));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtGoldMotes, entity);
        var slipUid = Spawn(JadeSlipPrototype, Transform(entity).Coordinates);
        var manualComponent = EnsureComp<CultivationManualComponent>(slipUid);
        manualComponent.Law = rootLaw;
        manualComponent.Awakens = true;
        _inheritanceSystem.MarkSlip(slipUid, entity);
        _handsSystem.PickupOrDrop(entity, slipUid, checkActionBlocker: false);

        // Teaching is its own kind of insight.
        _sharedCultivationSystem.AddInsight(entity.AsNullable(), 10f);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-transmit-law", ("user", entity.Owner)), type: PopupType.Medium);
    }

    #endregion

    #region Concealment, Forced Circulation, letting go of a law

    [SubscribeLocalEvent]
    private void OnConcealment(Entity<CultivatorComponent> entity, ref ConcealmentActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtMistShadow, entity);
        if (RemComp<CultivationConcealedComponent>(entity))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-concealment-off"));
            return;
        }

        if (!_sharedCultivationSystem.TrySpendQi(entity.AsNullable(), args.QiCost))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-not-enough-qi"), type: PopupType.SmallCaution);
            return;
        }

        AddComp<CultivationConcealedComponent>(entity);
        _noticeSystem.Notify(entity, Loc.GetString("cultivation-concealment-on"), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnForcedCirculation(Entity<CultivatorComponent> entity, ref ForcedCirculationActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtGoldMotes, entity);
        var forced = !entity.Comp.ForcedCirculation;
        _sharedCultivationSystem.SetForcedCirculation(entity.AsNullable(), forced);
        _actionsSystem.SetToggled(args.Action.AsNullable(), forced);
        _noticeSystem.Notify(entity, Loc.GetString(forced ? "cultivation-forced-circulation-on" : "cultivation-forced-circulation-off"), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnForgetLaw(Entity<CultivatorComponent> entity, ref ForgetLawActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (entity.Comp.Laws.Count < 2)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-forget-nothing"));
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, entity, TimeSpan.FromSeconds(30), new ForgetLawDoAfterEvent(), entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _noticeSystem.Announce(entity, Loc.GetString("cultivation-forget-start", ("user", entity.Owner)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnForgetLawDoAfter(Entity<CultivatorComponent> entity, ref ForgetLawDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || entity.Comp.Laws.Count < 2)
            return;

        args.Handled = true;
        ForgetComplementaryLaw(entity);
    }

    /// <summary>
    ///     Let the Complementary Law drain out: its techniques go, and so does any combination it was half of.
    /// </summary>
    public bool ForgetComplementaryLaw(Entity<CultivatorComponent> entity)
    {
        if (entity.Comp.Laws.Count < 2)
            return false;

        var lawId = entity.Comp.Laws[1];
        if (!ProtoMan.TryIndex(lawId, out var law) || !_sharedCultivationSystem.TryForgetLaw(entity.AsNullable(), lawId))
            return false;

        var lost = new List<EntProtoId>();
        foreach (var technique in law.Techniques)
            lost.Add(technique.Action);

        var rootElement = _sharedCultivationSystem.GetRootLaw(entity.Comp) is { } rootId && ProtoMan.TryIndex(rootId, out var root)
            ? root.Element
            : (CultivationElement?)null;
        foreach (var combo in ProtoMan.EnumeratePrototypes<Content.Shared._KS14.Cultivation.Prototypes.CultivationComboPrototype>())
        {
            if ((combo.ElementOne == law.Element || combo.ElementTwo == law.Element) && law.Element != rootElement)
                lost.Add(combo.Action);
        }

        foreach (var actionProto in lost)
        {
            if (_sharedCultivationSystem.ForgetTechnique(entity.Comp, actionProto) is { } actionUid)
                _loadoutSystem.Forget(entity.Owner, actionUid);
        }

        _noticeSystem.Notify(entity, Loc.GetString("cultivation-forget-done", ("law", Loc.GetString(law.Name))), type: PopupType.Medium);
        return true;
    }

    #endregion

    #region Formations

    [SubscribeLocalEvent]
    private void OnInscribeFormation(Entity<CultivatorComponent> entity, ref InscribeFormationActionEvent args)
    {
        if (args.Handled || !_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var coordinates = Transform(entity).Coordinates.SnapToGrid(EntityManager);
        switch (args.Formation)
        {
            case CultivationFormation.Gathering:
                Spawn(GatheringArrayPrototype, coordinates);
                break;
            case CultivationFormation.Barrier:
                InscribeBarrier(entity, coordinates);
                break;
            case CultivationFormation.Alarm:
                var alarmUid = Spawn(AlarmArrayPrototype, coordinates);
                EnsureComp<AlarmArrayComponent>(alarmUid).Maker = entity;
                break;
        }

        _audioSystem.PlayPvs(WriteSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString($"cultivation-formation-{args.Formation}", ("user", entity.Owner)), type: PopupType.Medium);
    }

    /// <summary>
    ///     A ring of qi walls two tiles out, which only the maker and their friends can pass.
    /// </summary>
    private void InscribeBarrier(EntityUid makerUid, EntityCoordinates centre)
    {
        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != 2)
                    continue;

                var wallUid = Spawn(BarrierPrototype, centre.Offset(new Vector2((float)x, (float)y)));
                var barrierComponent = EnsureComp<QiBarrierComponent>(wallUid);
                barrierComponent.Maker = makerUid;
                Dirty(wallUid, barrierComponent);
            }
        }
    }

    private void ScanAlarm(Entity<AlarmArrayComponent> entity)
    {
        if (entity.Comp.Maker is not { } makerUid || TerminatingOrDeleted(makerUid))
            return;

        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(entity).Coordinates, AlarmRange, _mobs);
        foreach (var mobEntity in _mobs)
        {
            if (mobEntity.Owner == makerUid || _sharedCultivationSystem.IsAlly(makerUid, mobEntity.Owner) || !entity.Comp.Noticed.Add(mobEntity.Owner))
                continue;

            _noticeSystem.Notify(makerUid, Loc.GetString("cultivation-alarm-array-tripped", ("intruder", mobEntity.Owner)), type: PopupType.MediumCaution);
        }
    }

    #endregion
}

/// <summary>
///     Raised on someone whose acupoint is being sealed, before the usual effect. Set Handled to use the seal for
///         something else (unsealing an Exploding Heart Palm, say).
/// </summary>
[ByRefEvent]
public record struct AcupointSealedEvent(EntityUid Sealer, bool Handled = false);

/// <summary>
///     A talisman with a little of its writer's qi in it.
/// </summary>
[RegisterComponent]
public sealed partial class QiTalismanComponent : Component
{
    [DataField]
    public CultivationElement Element;
}

/// <summary>
///     An alarm array, whispering to its maker when strangers step inside.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(CultivationUniversalSystem))]
public sealed partial class AlarmArrayComponent : Component
{
    [DataField]
    public EntityUid? Maker;

    [DataField]
    public HashSet<EntityUid> Noticed = new();

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextScan;
}
