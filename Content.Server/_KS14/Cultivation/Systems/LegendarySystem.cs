using System.Numerics;
using Content.Server._KS14.Cultivation.Body;
using Content.Server._KS14.Cultivation.Components;
using Content.Server.Chat.Systems;
using Content.Server.Pinpointer;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Legendary weapons and treasures from Chinese legend and the martial novels, ported from SurfShack13.
///     They stand level with (or above) a Primordial Chaos Body: every blow from one is true damage that ignores
///         tempered flesh and armour, and nobody braces against them by realm. Each answers the body path in its own way
///         (<see cref="LegendaryTrait"/>). Their powers live in the partial files beside this one.
/// </summary>
public sealed partial class LegendarySystem : EntitySystem
{
    private static readonly SoundSpecifier BladeSound = new SoundPathSpecifier("/Audio/Weapons/bladeslice.ogg");
    private static readonly SoundSpecifier RepulseSound = new SoundPathSpecifier("/Audio/Magic/forcewall.ogg");

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private BodyArtSystem _bodyArtSystem = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private CultivationRubbleSystem _rubbleSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedBodyCultivationSystem _bodyCultivationSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Content.Shared.Verbs.GetVerbsEvent<Content.Shared.Verbs.AlternativeVerb>>(OnHideVerbs);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        UpdateTwinStorms(now);
        UpdateRuyiSlams(now);
        UpdateGourds(now);
        UpdatePouches(now);
        UpdatePagodas(now);
        UpdateRings(now);
        UpdateWheels();
        UpdateSevenStars(now);
    }

    #region Legendary blows

    /// <summary>
    ///     A legendary blow: true damage, no armour, no tempered flesh, no realm bracing. What it does to a body
    ///         cultivator depends on the artifact's traits.
    /// </summary>
    public bool LegendaryHit(EntityUid userUid, EntityUid victimUid, float damage, TimeSpan knockdown, EntityUid? artifactUid)
    {
        if (victimUid == userUid || TerminatingOrDeleted(victimUid) || !HasComp<MobStateComponent>(victimUid) || _mobStateSystem.IsDead(victimUid))
            return false;

        // Never your own hair clones.
        if (TryComp<SageCloneComponent>(victimUid, out var clone) && clone.Master == userUid)
            return false;

        var traits = artifactUid is { } uid && TryComp<LegendaryArtifactComponent>(uid, out var artifactComponent) ? artifactComponent.Traits : LegendaryTrait.None;
        if (TryComp<BodyCultivatorComponent>(victimUid, out var bodyComponent) && bodyComponent.Stage > 0)
        {
            if (traits.HasFlag(LegendaryTrait.CrushesTempered))
                damage += 3f * bodyComponent.Stage;

            if (traits.HasFlag(LegendaryTrait.WearsDown))
                _bodyCultivationSystem.AddExhaustion((victimUid, bodyComponent), 10f + 2f * bodyComponent.Stage);

            if (traits.HasFlag(LegendaryTrait.BreaksDefence) && _bodyCultivationSystem.BreakHardening(victimUid))
                _noticeSystem.Announce(victimUid, Loc.GetString("legendary-cuts-golden-body", ("target", victimUid)), type: PopupType.MediumCaution);

            if (traits != LegendaryTrait.None)
                _visualsSystem.Spark(victimUid, FxColour.Gold);
        }

        _damageableSystem.TryChangeDamage(victimUid, CultivationSystem.Damage("Slash", damage), ignoreResistances: true, origin: userUid);
        if (knockdown > TimeSpan.Zero)
            _stunSystem.TryKnockdown(victimUid, knockdown);

        return true;
    }

    /// <summary>
    ///     Every melee blow from a legendary artifact lands its true damage too, and then whatever that artifact does.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnArtifactMeleeHit(Entity<LegendaryArtifactComponent> entity, ref MeleeHitEvent args)
    {
        // The six treasures work out their own blows.
        if (!args.IsHit || args.HitEntities.Count == 0 || HasComp<LegendaryTreasureHitComponent>(entity))
            return;

        // A shrunken staff is just a needle.
        if (TryComp<RuyiStaffComponent>(entity, out var ruyiComponent) && !ruyiComponent.Extended)
            return;

        foreach (var victimUid in args.HitEntities)
        {
            if (!HasComp<MobStateComponent>(victimUid))
            {
                OnStructureHit(entity, victimUid, args.User);
                continue;
            }

            if (TryComp<DragonSaberComponent>(entity, out var saberComponent) && _gameTiming.CurTime < saberComponent.AwakeUntil)
            {
                saberComponent.AwakeUntil = TimeSpan.Zero;
                DragonSlayingStrike((entity, saberComponent), args.User, victimUid);
                continue;
            }

            var damage = entity.Comp.HitDamage;
            if (TryComp<TwinSwordComponent>(entity, out var twinComponent) && Paired((entity, twinComponent), args.User))
                damage = twinComponent.PairedDamage;

            if (!LegendaryHit(args.User, victimUid, damage, entity.Comp.HitKnockdown, entity))
                continue;

            if (entity.Comp.HitThrow > 0f)
                Hurl(victimUid, args.User, entity.Comp.HitThrow);

            if (saberComponent != null)
                _bodyArtSystem.Crack(_transformSystem.GetMapCoordinates(victimUid), 0.6f, 0.7f);
        }
    }

    /// <summary>
    ///     The Heaven Reliant Sword cuts iron like mud: doors, windows, machines and walls give way under it.
    /// </summary>
    private void OnStructureHit(Entity<LegendaryArtifactComponent> entity, EntityUid targetUid, EntityUid userUid)
    {
        if (!HasComp<HeavenReliantComponent>(entity))
            return;

        _bodyArtSystem.Smash(_transformSystem.GetMapCoordinates(targetUid), 0.3f, 400f, wallTier: 1, userUid);
        _visualsSystem.Art(CultivationVisualsSystem.ArtFistQi, targetUid);
    }

    private void Hurl(EntityUid victimUid, EntityUid fromUid, float distance)
    {
        var away = _transformSystem.GetMapCoordinates(victimUid).Position - _transformSystem.GetMapCoordinates(fromUid).Position;
        if (away.LengthSquared() < 0.0001f)
            away = _robustRandom.NextVector2(1f);

        _throwingSystem.TryThrow(victimUid, Vector2.Normalize(away) * distance, baseThrowSpeed: 12f, user: fromUid, recoil: false);
    }

    /// <summary>
    ///     A legendary blade of force down a line: everyone in it struck with true damage and hurled along it, everything
    ///         in it smashed.
    /// </summary>
    public void LegendaryLine(EntityUid userUid, EntityUid artifactUid, Vector2 direction, int length, int halfWidth, float damage, int wallTier)
    {
        var mapId = _transformSystem.GetMapId(userUid);
        var origin = _transformSystem.GetMapCoordinates(userUid).Position;
        direction = Vector2.Normalize(direction);
        var side = new Vector2(-direction.Y, direction.X);
        var hit = new HashSet<EntityUid>();
        for (var step = 1; step <= length; step++)
        {
            for (var offset = -halfWidth; offset <= halfWidth; offset++)
            {
                var point = new MapCoordinates(origin + direction * step + side * offset, mapId);
                _bodyArtSystem.Smash(point, 0.6f, 200f, wallTier, userUid);
                if (offset == 0 && step % 2 == 0)
                    _visualsSystem.Art(CultivationVisualsSystem.ArtFistQi, point);

                _mobs.Clear();
                _entityLookupSystem.GetEntitiesInRange(point, 0.6f, _mobs);
                foreach (var mob in _mobs)
                {
                    if (!hit.Add(mob.Owner) || !LegendaryHit(userUid, mob.Owner, damage, TimeSpan.FromSeconds(2), artifactUid))
                        continue;

                    _throwingSystem.TryThrow(mob.Owner, direction * 3f, baseThrowSpeed: 10f, user: userUid, recoil: false);
                }
            }
        }
    }

    #endregion

    #region Examining and omens

    /// <summary>
    ///     The first time someone picks a legendary up, it tells them how it's used.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnArtifactPickedUp(Entity<LegendaryArtifactComponent> entity, ref GotEquippedHandEvent args)
    {
        if (entity.Comp.Controls is not { } controls || !entity.Comp.Told.Add(args.User))
            return;

        var text = Robust.Shared.Utility.FormattedMessage.RemoveMarkupPermissive(Loc.GetString(controls));
        _noticeSystem.Notify(args.User, Loc.GetString("legendary-how-to", ("item", Name(entity)), ("controls", text)), PopupType.Medium, alsoToChat: true);
    }

    [SubscribeLocalEvent]
    private void OnArtifactExamined(Entity<LegendaryArtifactComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(entity.Comp.Power));
        if (entity.Comp.Controls is { } controls)
            args.PushMarkup(Loc.GetString(controls));
        args.PushMarkup(Loc.GetString("legendary-true-damage"));
        if (entity.Comp.Traits.HasFlag(LegendaryTrait.BreaksDefence))
            args.PushMarkup(Loc.GetString("legendary-trait-breaks-defence"));

        if (entity.Comp.Traits.HasFlag(LegendaryTrait.WearsDown))
            args.PushMarkup(Loc.GetString("legendary-trait-wears-down"));

        if (entity.Comp.Traits.HasFlag(LegendaryTrait.CrushesTempered))
            args.PushMarkup(Loc.GetString("legendary-trait-crushes-tempered"));

        if (HasComp<CultivatorComponent>(args.Examiner) || HasComp<BodyCultivatorComponent>(args.Examiner))
            args.PushMarkup(Loc.GetString("legendary-legend", ("legend", Loc.GetString(entity.Comp.Legend))));
    }

    /// <summary>
    ///     Every cultivator feels roughly where unclaimed legendary artifacts lie.
    /// </summary>
    public int Omen(List<EntityUid> sensers)
    {
        var places = new HashSet<string>();
        var query = EntityQueryEnumerator<LegendaryArtifactComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transformComponent))
        {
            if (HasComp<MapGridComponent>(transformComponent.ParentUid))
                places.Add(_noticeSystem.Where(uid));
        }

        if (places.Count == 0)
            return 0;

        var where = string.Join(", ", places);
        foreach (var senserUid in sensers)
            _noticeSystem.Notify(senserUid, Loc.GetString("legendary-omen", ("where", where)), PopupType.Large, alsoToChat: true);

        return places.Count;
    }

    #endregion

    /// <summary>
    ///     Shout the move's name, the way wuxia heroes do.
    /// </summary>
    private void Shout(EntityUid uid, string locId)
    {
        _chatSystem.TrySendInGameICMessage(uid, Loc.GetString(locId), Content.Shared.Chat.InGameICChatType.Speak, hideChat: false, ignoreActionBlocker: true);
    }
}
