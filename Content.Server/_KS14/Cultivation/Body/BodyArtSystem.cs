using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Chat;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Item;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Server.Decals;
using Content.Shared._KS14.Explosion.Shockwave;
using Content.Shared.Camera;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Cultivation.Body;

/// <summary>
///     The Body Molding martial arts: big, loud, earth-shattering moves, every one of them shouted like in the novels.
///     Everything scales hard with stage. From Vajra Viscera (6) plain walls give way; from Undying Flesh (8) even
///         reinforced walls do. Anyone of a higher realm braces against the blows: half damage, no knockdown.
/// </summary>
public sealed partial class BodyArtSystem : EntitySystem
{
    private static readonly SoundSpecifier ImpactSound = new SoundPathSpecifier("/Audio/Effects/metal_thud3.ogg");
    private static readonly SoundSpecifier PunchSound = new SoundPathSpecifier("/Audio/Effects/hit_kick.ogg");
    private static readonly SoundSpecifier RumbleSound = new SoundPathSpecifier("/Audio/Magic/rumble.ogg");
    private static readonly SoundSpecifier BreathSound = new SoundPathSpecifier("/Audio/Magic/staff_healing.ogg");
    private static readonly SoundSpecifier BellSound = new SoundPathSpecifier("/Audio/Effects/church_bell3.ogg");
    private static readonly SoundSpecifier RoarSound = new SoundPathSpecifier("/Audio/Animals/lizard_happy.ogg");
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly EntProtoId ShockwaveEffect = "KsBodyShockwave";
    private static readonly EntProtoId DustEffect = "KsBodyDust";
    private static readonly string[] CrackDecals =
    {
        "KsCrack1", "KsCrack2", "KsCrack3", "KsCrack4", "KsCrack5", "KsCrack6",
        "KsCrack7", "KsCrack8", "KsCrack9", "KsCrack10", "KsCrack11", "KsCrack12",
    };

    /// <summary>
    ///     Every body art hits this much harder than its listed base. The arts were tuned too gently.
    /// </summary>
    private const float ArtDamageMultiplier = 1.6f;

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private Content.Shared.Actions.SharedActionsSystem _actionsSystem = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private DecalSystem _decalSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private CultivationRubbleSystem _rubbleSystem = default!;
    [Dependency] private SharedCameraRecoilSystem _cameraRecoilSystem = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private BodyCultivationSystem _bodyCultivationSystem = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private SharedBodyCultivationSystem _bodySystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private InheritanceSystem _inheritanceSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TagSystem _tagSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private SharedMeleeWeaponSystem _meleeWeaponSystem = default!;
    [Dependency] private EntityQuery<BodyCultivatorComponent> _bodyCultivatorQuery = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();
    private readonly HashSet<Entity<DamageableComponent>> _breakables = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _gameTiming.CurTime;
        var quakeEnumerator = EntityQueryEnumerator<BodyQuakeComponent>();
        while (quakeEnumerator.MoveNext(out var uid, out var quakeComponent))
        {
            if (currentTime < quakeComponent.NextPulse)
                continue;

            quakeComponent.NextPulse = currentTime + TimeSpan.FromSeconds(1);
            QuakePulse(uid, quakeComponent);
            if (--quakeComponent.PulsesLeft <= 0)
                RemCompDeferred(uid, quakeComponent);
        }

        var flurryEnumerator = EntityQueryEnumerator<BodyFlurryComponent>();
        while (flurryEnumerator.MoveNext(out var uid, out var flurryComponent))
        {
            if (currentTime < flurryComponent.NextBlow)
                continue;

            flurryComponent.NextBlow = currentTime + FlurryInterval;
            FlurryBlow((uid, flurryComponent));
        }
    }

    #region Helpers

    /// <summary>
    ///     Pay a body art's exhaustion, or say why not.
    /// </summary>
    private bool TryExert(Entity<BodyCultivatorComponent> entity, float exhaustion, FxColour colour = FxColour.Earth)
    {
        if (_cultivationSystem.IsSealed(entity))
            return false;

        if (!_bodySystem.CanExert(entity.Comp, exhaustion))
        {
            _noticeSystem.Notify(entity, Loc.GetString("body-art-too-exhausted",
                ("exhaustion", (int)entity.Comp.Exhaustion), ("cost", (int)exhaustion)), type: PopupType.SmallCaution);
            return false;
        }

        _bodySystem.AddExhaustion(entity.AsNullable(), exhaustion);
        _bodySystem.SetLastArt(entity.Comp, _gameTiming.CurTime);
        _visualsSystem.Cast(entity, colour);
        // Practising an art pushes the body too.
        _bodySystem.AddTempering(entity.AsNullable(), exhaustion * 0.2f * BodyCultivationSystem.TrainingRate, trainingSource: "practice", cooldown: TimeSpan.FromSeconds(5));
        return true;
    }

    /// <summary>
    ///     Wuxia heroes announce their moves.
    /// </summary>
    private void Shout(EntityUid userUid, string artLoc)
    {
        _chatSystem.TrySendInGameICMessage(userUid, Loc.GetString(artLoc).ToUpperInvariant() + "!!", InGameICChatType.Speak,
            hideChat: false, ignoreActionBlocker: true);
    }

    /// <summary>
    ///     A rough measure of how far along the path someone is, for bracing: body stage, or two per qi realm.
    /// </summary>
    private int PathPower(EntityUid uid)
    {
        if (_bodyCultivatorQuery.TryComp(uid, out var bodyComponent))
            return bodyComponent.Stage;

        if (_cultivatorQuery.TryComp(uid, out var cultivatorComponent))
            return 1 + 2 * (int)cultivatorComponent.Realm;

        return 0;
    }

    /// <summary>
    ///     Hit someone with a body art. Those further along the path brace: half damage, and they keep their feet.
    /// </summary>
    /// <returns>True if the blow landed in full.</returns>
    public bool Hit(EntityUid userUid, EntityUid victimUid, float damage, TimeSpan knockdown)
    {
        if (victimUid == userUid || !_mobStateQuery.TryComp(victimUid, out var mobStateComponent) || _mobStateSystem.IsDead(victimUid, component: mobStateComponent))
            return false;

        damage *= ArtDamageMultiplier;

        // The ninth stage is the end of the path: the stages before it hold back, so it stands clearly above them.
        if (_bodyCultivatorQuery.TryComp(userUid, out var userBody))
            damage *= SharedBodyCultivationSystem.StageStrength(userBody.Stage);
        _visualsSystem.Strike(userUid, victimUid, FxColour.Earth);
        if (PathPower(victimUid) > PathPower(userUid))
        {
            _damageableSystem.TryChangeDamage(victimUid, CultivationSystem.Damage("Blunt", damage / 2f), origin: userUid);
            return false;
        }

        _damageableSystem.TryChangeDamage(victimUid, CultivationSystem.Damage("Blunt", damage), origin: userUid);
        if (knockdown > TimeSpan.Zero)
            _stunSystem.TryKnockdown(victimUid, knockdown);

        return true;
    }

    private void HurlFrom(EntityUid victimUid, Vector2 origin, float distance, EntityUid userUid, float speed = 10f)
    {
        var direction = _transformSystem.GetMapCoordinates(victimUid).Position - origin;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        _throwingSystem.TryThrow(victimUid, Vector2.Normalize(direction) * distance, baseThrowSpeed: speed, user: userUid, recoil: false);
    }

    /// <summary>
    ///     Whether something solid and still standing (not already broken and on its way out) fills this spot.
    /// </summary>
    private bool Blocked(MapCoordinates point, EntityUid userUid)
    {
        _breakables.Clear();
        _entityLookupSystem.GetEntitiesInRange(point, 0.3f, _breakables, flags: LookupFlags.Static);
        foreach (var blockerEntity in _breakables)
        {
            if (blockerEntity.Owner != userUid && !TerminatingOrDeleted(blockerEntity) && !EntityManager.IsQueuedForDeletion(blockerEntity)
                && _tagSystem.HasTag(blockerEntity.Owner, WallTag))
                return true;
        }

        return false;
    }

    private List<EntityUid> MobsAround(MapCoordinates centre, float radius, EntityUid exclude)
    {
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, radius, _mobs);
        var found = new List<EntityUid>();
        foreach (var mobEntity in _mobs)
        {
            if (mobEntity.Owner != exclude)
                found.Add(mobEntity.Owner);
        }

        return found;
    }

    #region Effects

    /// <summary>
    ///     What a heavy blow to the world leaves: a shockwave rippling out, cracked floor, dust, and the camera of
    ///         everyone close by jolted.
    /// </summary>
    /// <param name="strength">Roughly 1 for a stomp, up to 3 for the arts that shake the whole area.</param>
    public void Impact(MapCoordinates centre, float radius, float strength, bool shockwave = true, int crater = -1)
    {
        if (shockwave)
        {
            var waveUid = Spawn(ShockwaveEffect, centre);
            var waveComponent = EnsureComp<KsShockwaveComponent>(waveUid);
            waveComponent.StartTime = _gameTiming.CurTime;
            waveComponent.Width = 0.6f + 0.3f * strength;
            waveComponent.FalloffPower = MathF.Max(10f, 40f - 8f * strength);
            Dirty(waveUid, waveComponent);
        }

        _visualsSystem.WaveAt(centre, FxColour.Earth, big: strength >= 1.5f);
        Crater(centre, crater >= 0 ? crater : strength < 1.5f ? 0 : strength < 2.5f ? 1 : 2);
        Crack(centre, radius, chance: 0.35f + 0.15f * strength);

        var puffs = (int)MathF.Ceiling(2f * strength);
        for (var puff = 0; puff < puffs; puff++)
            Spawn(DustEffect, centre.Offset(_robustRandom.NextVector2(radius * 0.6f)));

        // Everyone nearby feels it underfoot.
        foreach (var feelerUid in MobsAround(centre, radius * 2.5f + 2f, EntityUid.Invalid))
        {
            if (!HasComp<ActorComponent>(feelerUid))
                continue;

            var away = _transformSystem.GetMapCoordinates(feelerUid).Position - centre.Position;
            var kick = away.LengthSquared() < 0.01f ? _robustRandom.NextVector2(1f) : Vector2.Normalize(away);
            _cameraRecoilSystem.KickCamera(feelerUid, kick * (0.25f * strength));
        }
    }

    /// <summary>
    ///     A crater punched into the floor right where the blow landed.
    /// </summary>
    public void Crater(MapCoordinates centre, int size)
    {
        // As in SS13 it lingers for half a minute, centred on the tile that took the blow, at a random turn.
        if (TileCentre(centre) is { } tileCentre)
            _visualsSystem.Crater(_transformSystem.ToMapCoordinates(tileCentre), size);
    }

    /// <summary>
    ///     The centre of the floor tile under a point, or null out in space.
    /// </summary>
    private EntityCoordinates? TileCentre(MapCoordinates point)
    {
        if (!_mapSystem.TryFindGridAt(point, out var gridUid, out var gridComponent))
            return null;

        return _mapSystem.GridTileToLocal(gridUid, gridComponent, _mapSystem.TileIndicesFor(gridUid, gridComponent, point));
    }

    /// <summary>
    ///     Leave cracks on the floor tiles around a point. Janitors can scrub them away.
    /// </summary>
    public void Crack(MapCoordinates centre, float radius, float chance)
    {
        // Wide blasts scatter their cracks rather than papering every tile.
        chance *= MathF.Min(1f, 3f / MathF.Max(radius, 1f));
        var reach = (int)MathF.Ceiling(radius);
        for (var x = -reach; x <= reach; x++)
        {
            for (var y = -reach; y <= reach; y++)
            {
                if (x * x + y * y > radius * radius + 0.5f || !_robustRandom.Prob(chance))
                    continue;

                var point = new MapCoordinates(centre.Position + new Vector2((float)x, (float)y), centre.MapId);
                if (TileCentre(point) is not { } tileCentre)
                    continue;

                // Decals are placed by their bottom-left corner.
                var snapped = tileCentre.Offset(new Vector2(-0.5f, -0.5f));
                _decalSystem.TryAddDecal(_robustRandom.Pick(CrackDecals), snapped, out _,
                    rotation: Angle.FromDegrees(90 * _robustRandom.Next(4)), cleanable: true);
            }
        }
    }

    #endregion

    /// <summary>
    ///     What walls a stage can break: 0 none, 1 plain walls, 2 reinforced walls too.
    /// </summary>
    public static int WallTier(int stage)
    {
        return stage >= 7 ? 2 : (stage >= 5 ? 1 : 0);
    }

    /// <summary>
    ///     How hard a stage's arts hit structures: about a third at the first stage, about the old figure in the middle
    ///         stages, a little over twice it at the ninth.
    /// </summary>
    public static float Destruction(int stage)
    {
        return SharedBodyCultivationSystem.StageStrength(stage) * (0.5f + 0.02f * stage * stage);
    }

    /// <summary>
    ///     Smash everything breakable around a point: windows, grilles, tables and machines always; walls by tier.
    ///         Below the tier that breaks them, walls still take <paramref name="wallChip"/>, so a few blows bring one down.
    /// </summary>
    public void Smash(MapCoordinates centre, float radius, float power, int wallTier, EntityUid userUid, float wallChip = 0f)
    {
        // Destruction grows with the square of the stage, as the blows against people do, and the stages before the
        //     ninth hold back the same way.
        if (_bodyCultivatorQuery.TryComp(userUid, out var userBody))
        {
            var scale = Destruction(userBody.Stage);
            power *= scale;
            wallChip *= scale;
        }

        Crack(centre, radius, chance: 0.5f);
        // Note what's already lying loose here, so only the debris this blow knocks free is flung away.
        _debrisBefore.Clear();
        _debris.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, radius + DebrisMargin, _debris, flags: LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var item in _debris)
            _debrisBefore.Add(item.Owner);

        _breakables.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, radius, _breakables, flags: LookupFlags.Static | LookupFlags.Dynamic);
        foreach (var breakableEntity in _breakables)
        {
            if (breakableEntity.Owner == userUid || _mobStateQuery.HasComp(breakableEntity.Owner))
                continue;

            // A body that breaks walls breaks everything lesser too: doors, windows, machines, girders.
            var damage = wallTier >= 2 ? MathF.Max(power, 2500f) : (wallTier == 1 ? MathF.Max(power, 700f) : power);
            if (_tagSystem.HasTag(breakableEntity.Owner, WallTag))
            {
                // As in SS13, walls come down into rubble rather than vanishing: the first tier knocks a plain wall
                //     to its girder with half its plating strewn about, the second devastates any wall to scrap.
                //     Below that, or against a reinforced wall at the first tier, it only chips at it.
                var sturdy = _rubbleSystem.IsReinforced(breakableEntity.Owner);
                if (wallTier >= 2 || wallTier == 1 && !sturdy)
                {
                    _rubbleSystem.BreakWall(breakableEntity.Owner, devastate: wallTier >= 2, userUid);
                    continue;
                }

                damage = wallTier == 1 ? MathF.Max(wallChip, 300f) : wallChip;
                if (damage <= 0f)
                    continue;
            }
            else if (_tagSystem.HasTag(breakableEntity.Owner, WindowTag))
            {
                damage = MathF.Max(damage, 300f);
            }

            // Never so hard that it vanishes: it breaks into its shards, sheets and parts.
            damage = _rubbleSystem.Capped(breakableEntity.Owner, damage);
            if (damage <= 0f)
                continue;

            if (_damageableSystem.TryChangeDamage(breakableEntity.Owner, CultivationSystem.Damage("Structural", damage), origin: userUid)
                && _robustRandom.Prob(0.5f))
                _rubbleSystem.Rubble(_transformSystem.GetMapCoordinates(breakableEntity.Owner));
        }

        FlingDebris(centre, radius, userUid);
    }

    private const float DebrisMargin = 1f;
    private const int MaxDebrisFlung = 12;
    private readonly HashSet<Entity<ItemComponent>> _debris = new();
    private readonly HashSet<EntityUid> _debrisBefore = new();

    /// <summary>
    ///     Sheets, rods, shards and parts knocked loose by a blow scatter away from whoever struck it: a short toss, and
    ///         only a handful per blow, so a wrecked room doesn't flood the server with flying scrap.
    /// </summary>
    private void FlingDebris(MapCoordinates centre, float radius, EntityUid userUid)
    {
        _debris.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, radius + DebrisMargin, _debris, flags: LookupFlags.Dynamic | LookupFlags.Sundries);
        var from = _transformSystem.GetMapCoordinates(userUid);
        var flung = 0;
        foreach (var item in _debris)
        {
            if (flung >= MaxDebrisFlung)
                break;

            if (_debrisBefore.Contains(item.Owner) || TerminatingOrDeleted(item.Owner) || Transform(item.Owner).Anchored)
                continue;

            var position = _transformSystem.GetMapCoordinates(item.Owner).Position;
            var away = from.MapId == centre.MapId ? position - from.Position : position - centre.Position;
            if (away.LengthSquared() < 0.01f)
                away = _robustRandom.NextAngle().ToVec();

            // Mostly away from the blow, scattered a little either side.
            var direction = Vector2.Normalize(away);
            direction = new Angle(_robustRandom.NextFloat(-0.5f, 0.5f)).RotateVec(direction);
            _throwingSystem.TryThrow(item.Owner, direction * _robustRandom.NextFloat(1.5f, 3f), baseThrowSpeed: 6f, recoil: false);
            flung++;
        }

        _debrisBefore.Clear();
    }

    /// <summary>
    ///     A shockwave tearing down a line: everyone in it is hit and hurled along it, and it smashes what it passes.
    /// </summary>
    public void ShatterLine(EntityUid userUid, Vector2 origin, Vector2 direction, int length, int halfWidth, float damage, int wallTier)
    {
        var mapId = _transformSystem.GetMapId(userUid);
        direction = Vector2.Normalize(direction);
        var side = new Vector2(-direction.Y, direction.X);
        var hit = new HashSet<EntityUid>();

        for (var step = 1; step <= length; step++)
        {
            for (var offset = -halfWidth; offset <= halfWidth; offset++)
            {
                var point = new MapCoordinates(origin + direction * step + side * offset, mapId);
                Smash(point, 0.6f, 200f, wallTier, userUid, wallChip: 150f);
                if (offset == 0)
                    Spawn(DustEffect, point);
                foreach (var victimUid in MobsAround(point, 0.6f, userUid))
                {
                    if (!hit.Add(victimUid))
                        continue;

                    if (Hit(userUid, victimUid, damage, TimeSpan.FromSeconds(2)))
                        _throwingSystem.TryThrow(victimUid, direction * 3f, baseThrowSpeed: 10f, user: userUid, recoil: false);
                }
            }
        }
    }

    private Vector2 Towards(EntityUid userUid, EntityCoordinates targetUid, out float distance)
    {
        var offset = _transformSystem.ToMapCoordinates(targetUid).Position - _transformSystem.GetMapCoordinates(userUid).Position;
        distance = offset.Length();
        return distance > 0.0001f ? offset / distance : new Vector2(0f, 1f);
    }

    /// <summary>
    ///     Throw someone (often oneself) through the air to land with a body art's force.
    /// </summary>
    private void Launch(EntityUid thrownUid, Vector2 offset, float speed, EntityUid sourceUid, BodyLandingComponent landingTemplate)
    {
        var landingComponent = EnsureComp<BodyLandingComponent>(thrownUid);
        landingComponent.Source = sourceUid;
        landingComponent.Radius = landingTemplate.Radius;
        landingComponent.Damage = landingTemplate.Damage;
        landingComponent.CentreDamage = landingTemplate.CentreDamage;
        landingComponent.Knockdown = landingTemplate.Knockdown;
        landingComponent.Smash = landingTemplate.Smash;
        landingComponent.HurtSelf = landingTemplate.HurtSelf;
        landingComponent.ArtName = landingTemplate.ArtName;
        _throwingSystem.TryThrow(thrownUid, offset, baseThrowSpeed: speed, user: sourceUid == thrownUid ? null : sourceUid, recoil: false, doSpin: thrownUid != sourceUid);
    }

    [SubscribeLocalEvent]
    private void OnLand(Entity<BodyLandingComponent> entity, ref LandEvent args)
    {
        var landingComponent = entity.Comp;
        RemCompDeferred(entity, landingComponent);
        var sourceUid = landingComponent.Source ?? entity.Owner;
        var centre = _transformSystem.GetMapCoordinates(entity);

        if (landingComponent.HurtSelf)
        {
            Hit(sourceUid, entity, landingComponent.Damage, landingComponent.Knockdown);
        }
        else if (landingComponent.Radius > 0f)
        {
            foreach (var victimUid in MobsAround(centre, landingComponent.Radius, entity))
            {
                var near = _transformSystem.InRange((victimUid, null), (entity.Owner, null), 0.75f);
                Hit(sourceUid, victimUid, near ? landingComponent.Damage + landingComponent.CentreDamage : landingComponent.Damage, landingComponent.Knockdown);
            }
        }

        if (landingComponent.Smash > 0f)
            Smash(centre, MathF.Max(landingComponent.Radius, 1f), landingComponent.Smash, 0, sourceUid);

        if (landingComponent.Radius > 0f || landingComponent.HurtSelf)
        {
            _audioSystem.PlayPvs(ImpactSound, entity);
            Impact(centre, MathF.Max(landingComponent.Radius, 1f), strength: landingComponent.HurtSelf ? 0.5f : 1.5f);
            _visualsSystem.Art(CultivationVisualsSystem.ArtFistEarth, centre);
        }
    }

    #endregion

    #region Stage 1-2: Breath of Renewal, Iron Shirt, Mountain Leap, Earth-Shattering Stomp

    [SubscribeLocalEvent]
    private void OnBreathOfRenewal(Entity<BodyCultivatorComponent> entity, ref BodyBreathOfRenewalActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Wood))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtMendWood, entity);
        var breathingComponent = EnsureComp<BodyBreathingComponent>(entity);
        breathingComponent.BreathsLeft = 2;
        breathingComponent.ExhaustionPerBreath = args.Exhaustion;
        StartBreath(entity);
    }

    private void StartBreath(EntityUid userUid)
    {
        var doAfterArgs = new DoAfterArgs(EntityManager, userUid, TimeSpan.FromSeconds(2), new BodyBreathDoAfterEvent(), userUid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };
        _doAfterSystem.TryStartDoAfter(doAfterArgs);
    }

    [SubscribeLocalEvent]
    private void OnBreathDoAfter(Entity<BodyCultivatorComponent> entity, ref BodyBreathDoAfterEvent args)
    {
        if (args.Handled || !TryComp<BodyBreathingComponent>(entity, out var breathingComponent))
            return;

        args.Handled = true;
        if (args.Cancelled)
        {
            RemComp<BodyBreathingComponent>(entity);
            return;
        }

        var heal = 5f + 2f * (float)entity.Comp.Stage + (float)SharedBodyCultivationSystem.GetLevel(entity.Comp, BodyGroup.Heart);
        _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-heal * 2f));
        _audioSystem.PlayPvs(BreathSound, entity);

        // Up to three breaths; the first was paid for by casting, the rest cost their own.
        if (breathingComponent.BreathsLeft <= 0 || !_bodySystem.CanExert(entity.Comp, breathingComponent.ExhaustionPerBreath))
        {
            RemComp<BodyBreathingComponent>(entity);
            return;
        }

        breathingComponent.BreathsLeft--;
        _bodySystem.AddExhaustion(entity.AsNullable(), breathingComponent.ExhaustionPerBreath);
        args.Repeat = true;
    }

    [SubscribeLocalEvent]
    private void OnIronShirt(Entity<BodyCultivatorComponent> entity, ref BodyIronShirtActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Metal))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtSheenIron, entity);
        var duration = TimeSpan.FromSeconds(10 + entity.Comp.Stage);
        if (_bodySystem.GetStance(entity) == BodyStance.Mountain)
            duration *= 1.5;

        _bodySystem.ApplyIronShirt(entity, duration, 0.5f);
        Shout(entity, "body-art-iron-shirt");
        _noticeSystem.Announce(entity, Loc.GetString("body-art-iron-shirt-on", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnMountainLeap(Entity<BodyCultivatorComponent> entity, ref BodyMountainLeapActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Qi))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        var direction = Towards(entity, args.Target, out var distance);
        distance = MathF.Min(distance, (float)(3 + stage));

        // The landing hits everyone around it, harder the further along the path; from Crimson Blood it cracks the
        //     ground and floors them too.
        var landingComponent = new BodyLandingComponent();
        if (stage >= 2)
        {
            landingComponent.Radius = stage >= 5 ? 1f + (float)((stage - 5) / 2) : 0.8f;
            landingComponent.Damage = 4f + 2f * stage + 0.6f * stage * stage;
            landingComponent.Knockdown = stage >= 5 ? TimeSpan.FromSeconds(1 + stage * 0.1) : TimeSpan.Zero;
            landingComponent.Smash = stage >= 7 ? (float)(60 + 10 * stage) : 0f;
        }

        _visualsSystem.Play(CultivationVisualsSystem.QinggongSound, entity);
        Launch(entity, direction * distance, 8f + stage, entity, landingComponent);
    }

    [SubscribeLocalEvent]
    private void OnEarthStomp(Entity<BodyCultivatorComponent> entity, ref BodyEarthStompActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        _visualsSystem.Art(entity.Comp.Stage >= 6 ? CultivationVisualsSystem.ArtFissuresBig : CultivationVisualsSystem.ArtFissures, entity);
        var stage = entity.Comp.Stage;
        var centre = _transformSystem.GetMapCoordinates(entity);
        var radius = 2f + (float)stage / 3f;
        var mountain = _bodySystem.GetStance(entity) == BodyStance.Mountain;

        Shout(entity, "body-art-earth-stomp");
        foreach (var victimUid in MobsAround(centre, radius, entity))
        {
            if (Hit(entity, victimUid, (float)(10 + 3 * stage), TimeSpan.FromSeconds(1 + 0.2 * stage)) && mountain)
                HurlFrom(victimUid, centre.Position, 2f + stage / 3f, entity);
        }

        // The floor heaves: what stands around you breaks, and walls crack.
        Smash(centre, radius, (float)(30 + 15 * stage), WallTier(stage), entity, wallChip: (float)(25 * stage));
        Impact(centre, radius, strength: 1f + (float)stage / 4f);

        _audioSystem.PlayPvs(RumbleSound, entity);
    }

    #endregion

    #region Stage 3: Shattering Fist, Hundred Fist Barrage, Bone Setting, stances, disciples

    [SubscribeLocalEvent]
    private void OnShatteringFist(Entity<BodyCultivatorComponent> entity, ref BodyShatteringFistActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Gold))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtFistGold, args.Target);
        var stage = entity.Comp.Stage;
        var arms = SharedBodyCultivationSystem.GetLevel(entity.Comp, BodyGroup.Arms);
        var origin = _transformSystem.GetMapCoordinates(entity).Position;
        var direction = _transformSystem.GetMapCoordinates(args.Target).Position - origin;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        Shout(entity, "body-art-shattering-fist");
        _audioSystem.PlayPvs(PunchSound, args.Target);
        Impact(_transformSystem.GetMapCoordinates(args.Target), 0.8f, strength: 0.8f + (float)stage / 5f);

        if (_mobStateQuery.HasComp(args.Target))
        {
            if (Hit(entity, args.Target, (float)(15 + 2 * arms + 3 * stage), TimeSpan.FromSeconds(1 + 0.2 * stage)))
                _throwingSystem.TryThrow(args.Target, Vector2.Normalize(direction) * (3f + stage / 2f), baseThrowSpeed: 12f, user: entity, recoil: false);

            if (stage >= 7)
                ShatterLine(entity, _transformSystem.GetMapCoordinates(args.Target).Position, direction, stage - 4, stage >= 9 ? 1 : 0, (float)(8 + stage), WallTier(stage));
            return;
        }

        // At a wall, door or machine: from Jade Marrow the punch blasts a trench through it and what's behind it.
        var fistTier = stage >= 7 ? 2 : (stage >= 4 ? 1 : 0);
        // Below Jade Marrow a wall doesn't give way at once, but it cracks, and a few punches bring it down.
        Smash(_transformSystem.GetMapCoordinates(args.Target), 0.5f, (float)(60 + 20 * stage), fistTier, entity,
            wallChip: (float)(100 + 40 * stage));
        if (stage >= 4)
        {
            var depth = stage >= 9 ? 9 : (stage >= 8 ? 6 : (stage >= 7 ? 4 : stage - 3));
            ShatterLine(entity, origin, direction, depth, stage >= 9 ? 2 : (stage >= 7 ? 1 : 0), (float)(8 + stage), fistTier);
        }
    }

    [SubscribeLocalEvent]
    private void OnHundredFists(Entity<BodyCultivatorComponent> entity, ref BodyHundredFistsActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Metal))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtBarrage, args.Target);
        var stage = entity.Comp.Stage;
        var blows = 4 + stage;
        var perBlow = 3f + stage / 2f + entity.Comp.PunchDamage / 8f;
        if (_bodySystem.GetStance(entity) == BodyStance.Thunderclap)
            perBlow += 1f;

        Shout(entity, "body-art-hundred-fists");

        // The blows rain down one after another, a few every second, rather than all at once.
        var flurryComponent = EnsureComp<BodyFlurryComponent>(entity);
        flurryComponent.Target = args.Target;
        flurryComponent.BlowsLeft = blows;
        flurryComponent.PerBlow = perBlow;
        flurryComponent.NextBlow = _gameTiming.CurTime;
        _noticeSystem.Announce(args.Target, Loc.GetString("body-art-hundred-fists-hit", ("user", entity.Owner), ("target", args.Target), ("blows", blows)), type: PopupType.MediumCaution);
    }

    private static readonly TimeSpan FlurryInterval = TimeSpan.FromSeconds(0.09);

    /// <summary>
    ///     One blow of Hundred Fists: a lunge, a hit, a crack. The last sends the target flying. The flurry ends early if
    ///         they get out of reach or the user goes down.
    /// </summary>
    private void FlurryBlow(Entity<BodyFlurryComponent> entity)
    {
        var userUid = entity.Owner;
        var targetUid = entity.Comp.Target;
        var userPosition = _transformSystem.GetMapCoordinates(userUid);
        if (TerminatingOrDeleted(targetUid) || !_mobStateSystem.IsAlive(userUid))
        {
            RemCompDeferred<BodyFlurryComponent>(userUid);
            return;
        }

        var targetPosition = _transformSystem.GetMapCoordinates(targetUid);
        if (targetPosition.MapId != userPosition.MapId
            || (targetPosition.Position - userPosition.Position).Length() > 2.5f)
        {
            RemCompDeferred<BodyFlurryComponent>(userUid);
            return;
        }

        var last = --entity.Comp.BlowsLeft <= 0;
        Lunge(userUid, targetPosition);
        _audioSystem.PlayPvs(PunchSound, targetUid, AudioParams.Default.WithVariation(0.2f).WithVolume(last ? 2f : -3f));
        if (!last)
        {
            Hit(userUid, targetUid, entity.Comp.PerBlow, TimeSpan.Zero);
            _staminaSystem.TakeStaminaDamage(targetUid, 4f, source: userUid);
            return;
        }

        RemCompDeferred<BodyFlurryComponent>(userUid);
        if (Hit(userUid, targetUid, entity.Comp.PerBlow * 3f, TimeSpan.FromSeconds(2)))
            HurlFrom(targetUid, userPosition.Position, 4f, userUid, speed: 22f);

        Impact(targetPosition, 0.6f, strength: 1f, shockwave: true);
        _visualsSystem.Art(CultivationVisualsSystem.ArtFistEarth, targetUid);
    }

    /// <summary>
    ///     The swing animation of a punch toward a point, as a normal melee attack shows it.
    /// </summary>
    private void Lunge(EntityUid userUid, MapCoordinates target)
    {
        var userTransform = Transform(userUid);
        var localPosition = Vector2.Transform(target.Position, _transformSystem.GetInvWorldMatrix(userTransform));
        if (localPosition.LengthSquared() <= 0f)
            return;

        localPosition = userTransform.LocalRotation.RotateVec(localPosition);
        if (localPosition.Length() > 0.8f)
            localPosition = localPosition.Normalized() * 0.8f;

        var angle = (target.Position - _transformSystem.GetWorldPosition(userTransform)).ToWorldAngle();
        _meleeWeaponSystem.DoLunge(userUid, userUid, angle, localPosition, "WeaponArcFist", predicted: false);
    }

    [SubscribeLocalEvent]
    private void OnBoneSetting(Entity<BodyCultivatorComponent> entity, ref BodyBoneSettingActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Wood))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtMendBone, entity);
        // Wrench dislocations and fractures back into place: it heals blunt trauma above all.
        var heal = 10f + 3f * (float)entity.Comp.Stage;
        _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Blunt", -heal), ignoreResistances: true, interruptsDoAfters: false);
        _noticeSystem.Announce(entity, Loc.GetString("body-art-bone-setting", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnStance(Entity<BodyCultivatorComponent> entity, ref BodyStanceActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var current = _bodySystem.GetStance(entity);
        var next = args.Stance is { } chosen
            ? (current == chosen ? (BodyStance?) null : chosen)
            : current switch
            {
                null => BodyStance.Thunderclap,
                BodyStance.Thunderclap => BodyStance.Mountain,
                _ => null,
            };

        if (next is not { } stance)
        {
            _bodySystem.SetStance(entity, null);
            RefreshStanceIcon(entity);
            _noticeSystem.Notify(entity, Loc.GetString("body-stance-relax"));
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, entity, TimeSpan.FromSeconds(2), new BodyStanceDoAfterEvent(stance), entity)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _noticeSystem.Announce(entity, Loc.GetString("body-stance-settling", ("user", entity.Owner)), type: PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnStanceDoAfter(Entity<BodyCultivatorComponent> entity, ref BodyStanceDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        _bodySystem.SetStance(entity, args.Stance);
        RefreshStanceIcon(entity);
        _noticeSystem.Notify(entity, Loc.GetString($"body-stance-{args.Stance}"), type: PopupType.Medium);
    }

    public static readonly EntProtoId StanceAction = "ActionBodyStance";
    private static readonly SpriteSpecifier ThunderclapIcon = new SpriteSpecifier.Rsi(new ResPath("/Textures/_KS14/Cultivation/actions.rsi"), "body_thunderclap");
    private static readonly SpriteSpecifier MountainIcon = new SpriteSpecifier.Rsi(new ResPath("/Textures/_KS14/Cultivation/actions.rsi"), "body_mountain");

    /// <summary>
    ///     The Stance button shows the stance held, lit; out of any stance it shows the Thunderclap, dimmed, as the next.
    /// </summary>
    private void RefreshStanceIcon(Entity<BodyCultivatorComponent> entity)
    {
        if (_bodySystem.GetTechnique(entity.Comp, StanceAction) is not { } actionUid)
            return;

        var stance = _bodySystem.GetStance(entity);
        _actionsSystem.SetIcon(actionUid, stance == BodyStance.Mountain ? MountainIcon : ThunderclapIcon);
        _actionsSystem.SetIconColor(actionUid, stance == null ? Color.Gray : Color.White);
    }

    [SubscribeLocalEvent]
    private void OnDisciple(Entity<BodyCultivatorComponent> entity, ref BodyDiscipleActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var discipleUid = args.Target;

        // Only someone who has already set foot on the path (read the primer) can be taken on.
        if (!_bodyCultivatorQuery.TryComp(discipleUid, out var discipleComponent) || _cultivatorQuery.HasComp(discipleUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("body-disciple-not-on-path", ("target", discipleUid)));
            return;
        }

        if (discipleComponent.Committed)
        {
            _noticeSystem.Notify(entity, Loc.GetString("body-disciple-already", ("target", discipleUid)));
            return;
        }

        _bodySystem.Commit((discipleUid, discipleComponent));
        _bodySystem.AddTempering((discipleUid, discipleComponent), 30f);
        _inheritanceSystem.RecordDisciple(entity, discipleUid);
        _noticeSystem.Announce(discipleUid, Loc.GetString("body-disciple-accepted", ("user", entity.Owner), ("target", discipleUid)), type: PopupType.Large);
    }

    #endregion

    #region Stage 4-5: Remold Flesh, Raging Bull Charge, Blood Boil, Falling Mountain Descent, Mountain-Toppling Throw

    [SubscribeLocalEvent]
    private void OnRemold(Entity<BodyCultivatorComponent> entity, ref BodyRemoldActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Wood))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtMendWood, entity);
        // The Body Molding Art itself: flesh is clay to be shaped again.
        _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-(30f + 5f * (float)entity.Comp.Stage)));
        _noticeSystem.Announce(entity, Loc.GetString("body-art-remold", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnBullCharge(Entity<BodyCultivatorComponent> entity, ref BodyBullChargeActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Fire))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtMistDust, entity);
        var stage = entity.Comp.Stage;
        var origin = _transformSystem.GetMapCoordinates(entity);
        var direction = Towards(entity, args.Target, out _);
        var length = 4 + stage;

        Shout(entity, "body-art-bull-charge");

        // Bowl everyone in the path aside and burst through what's in the way, then follow through.
        var bowled = new HashSet<EntityUid>();
        var reached = origin;
        for (var step = 1; step <= length; step++)
        {
            var point = new MapCoordinates(origin.Position + direction * step, origin.MapId);
            // Nothing stops the bull: tables, machines, windows and walls in the way are simply broken.
            Smash(point, 0.6f, 2000f, 2, entity);
            Spawn(DustEffect, point);
            if (!Blocked(point, entity))
                reached = point;
            foreach (var victimUid in MobsAround(point, 0.8f, entity))
            {
                if (!bowled.Add(victimUid))
                    continue;

                if (Hit(entity, victimUid, 10f + 2f * stage + 0.5f * stage * stage, TimeSpan.FromSeconds(1.5)))
                    _throwingSystem.TryThrow(victimUid, new Vector2(-direction.Y, direction.X) * 2f, baseThrowSpeed: 10f, user: entity, recoil: false);
            }
        }

        // What was in the way is rubble by now, but still there until the tick ends, so the bull is carried to the
        //      far side rather than thrown into it.
        _transformSystem.SetWorldPosition(entity.Owner, reached.Position);
        Impact(reached, 1f, strength: 1f + (float)stage / 4f);
        _audioSystem.PlayPvs(RumbleSound, entity);
    }

    [SubscribeLocalEvent]
    private void OnBloodBoil(Entity<BodyCultivatorComponent> entity, ref BodyBloodBoilActionEvent args)
    {
        if (args.Handled || _bodySystem.InBloodBoil(entity))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtMistBlood, entity);
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtEmbers, entity);
        _bodySystem.ApplyBloodBoil(entity, TimeSpan.FromSeconds(15), args.CrashExhaustion);
        Shout(entity, "body-art-blood-boil");
        _noticeSystem.Announce(entity, Loc.GetString("body-art-blood-boil-on", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnBloodBoilEnded(Entity<BodyCultivatorComponent> entity, ref BodyBloodBoilEndedEvent args)
    {
        _noticeSystem.Notify(entity, Loc.GetString("body-art-blood-boil-crash"), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnFallingMountain(Entity<BodyCultivatorComponent> entity, ref BodyFallingMountainActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtFallingStar, args.Target);
        var stage = entity.Comp.Stage;
        var direction = Towards(entity, args.Target, out var distance);
        distance = MathF.Min(distance, 9f);

        Shout(entity, "body-art-falling-mountain");
        // A mountain doesn't drift down: it drops.
        Launch(entity, direction * distance, 30f, entity, new BodyLandingComponent
        {
            Radius = 2f,
            Damage = 10f + stage + 0.5f * stage * stage,
            CentreDamage = 10f + 2f * stage + 0.8f * stage * stage,
            Knockdown = TimeSpan.FromSeconds(2),
            Smash = (float)(80 + 10 * stage),
        });
    }

    [SubscribeLocalEvent]
    private void OnMountainHurl(Entity<BodyCultivatorComponent> entity, ref BodyMountainHurlActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<PullerComponent>(entity, out var pullerComponent) || pullerComponent.Pulling is not { } victimUid
            || !_mobStateQuery.HasComp(victimUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("body-art-hurl-needs-grab"));
            return;
        }

        if (!TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtFistEarth, args.Target);
        var stage = entity.Comp.Stage;
        if (TryComp<PullableComponent>(victimUid, out var pullableComponent))
            _pullingSystem.TryStopPull(victimUid, pullableComponent, entity);

        var direction = Towards(entity, args.Target, out var distance);
        distance = MathF.Min(distance, (float)(4 + stage));

        Shout(entity, "body-art-mountain-hurl");
        // Hurled like a stone from a catapult: they cross the room in a blink and hit the floor hard.
        Launch(victimUid, direction * distance, 30f, entity, new BodyLandingComponent
        {
            HurtSelf = true,
            Damage = (float)(10 + 2 * stage),
            Knockdown = TimeSpan.FromSeconds(2),
        });
    }

    #endregion

    #region Stage 6-9: Sky-Splitting Palm, Vajra Golden Body, Heaven-Shaking Quake, Primordial Roar

    [SubscribeLocalEvent]
    private void OnSkySplittingPalm(Entity<BodyCultivatorComponent> entity, ref BodySkySplittingPalmActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Sense))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtPalmGold, args.Target);
        var stage = entity.Comp.Stage;
        var direction = Towards(entity, args.Target, out _);

        Shout(entity, "body-art-sky-splitting-palm");
        ShatterLine(entity, _transformSystem.GetMapCoordinates(entity).Position, direction, 7 + 2 * stage, stage >= 9 ? 2 : (stage >= 7 ? 1 : 0),
            15f + 3f * stage + 0.9f * stage * stage, WallTier(stage));
        Impact(_transformSystem.GetMapCoordinates(entity), 1.5f, strength: 2f);
        _visualsSystem.Distort(_transformSystem.GetMapCoordinates(entity), FxDistortion.Push);
        _audioSystem.PlayPvs(RumbleSound, entity);
    }

    [SubscribeLocalEvent]
    private void OnVajra(Entity<BodyCultivatorComponent> entity, ref BodyVajraActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Gold))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtSheenGold, entity);
        _visualsSystem.Play(CultivationVisualsSystem.TempleBellSound, entity);
        var duration = TimeSpan.FromSeconds(8);
        if (_bodySystem.GetStance(entity) == BodyStance.Mountain)
            duration *= 1.5;

        _bodySystem.ApplyVajra(entity, duration);
        Shout(entity, "body-art-vajra");
        _audioSystem.PlayPvs(BellSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("body-art-vajra-on", ("user", entity.Owner)), type: PopupType.Large);
    }

    [SubscribeLocalEvent]
    private void OnHeavenQuake(Entity<BodyCultivatorComponent> entity, ref BodyHeavenQuakeActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        var quakeComponent = EnsureComp<BodyQuakeComponent>(entity);
        quakeComponent.PulsesLeft = 5;
        quakeComponent.Radius = 5f + (float)stage / 2f;
        quakeComponent.Damage = 8f + (float)stage;
        quakeComponent.NextPulse = _gameTiming.CurTime;

        Shout(entity, "body-art-heaven-quake");
    }

    private void QuakePulse(EntityUid userUid, BodyQuakeComponent quakeComponent)
    {
        var centre = _transformSystem.GetMapCoordinates(userUid);
        foreach (var victimUid in MobsAround(centre, quakeComponent.Radius, userUid))
            Hit(userUid, victimUid, quakeComponent.Damage, TimeSpan.FromSeconds(1.5));

        // The whole area comes apart a little more with every pulse: windows and furniture at once, walls cracking
        //      through, and by the fifth pulse there is not much left standing.
        var stage = _bodyCultivatorQuery.TryComp(userUid, out var bodyComponent) ? bodyComponent.Stage : 8;
        Smash(centre, quakeComponent.Radius, 400f, stage >= 9 ? 2 : 1, userUid);
        _visualsSystem.Distort(centre, FxDistortion.Pulse);
        Impact(centre, quakeComponent.Radius, strength: 2.5f);
        _visualsSystem.Art(CultivationVisualsSystem.ArtFissuresBig, centre);
        _audioSystem.PlayPvs(RumbleSound, userUid);
    }

    [SubscribeLocalEvent]
    private void OnPrimordialRoar(Entity<BodyCultivatorComponent> entity, ref BodyPrimordialRoarActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion, colour: FxColour.Blood))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtRoarBlood, entity);
        var centre = _transformSystem.GetMapCoordinates(entity);

        Shout(entity, "body-art-primordial-roar");
        // The roar of something that was here before the station and will be here after it. Everything within
        //      twelve tiles is flattened: people thrown, every wall, door, window and machine broken.
        const float RoarRadius = 12f;
        foreach (var victimUid in MobsAround(centre, RoarRadius, entity))
        {
            if (Hit(entity, victimUid, 30f, TimeSpan.FromSeconds(4)))
            {
                _staminaSystem.TakeStaminaDamage(victimUid, 60f, source: entity);
                HurlFrom(victimUid, centre.Position, 6f, entity);
            }
        }

        Smash(centre, RoarRadius, 3000f, 2, entity);
        Impact(centre, RoarRadius, strength: 3f);
        _visualsSystem.Distort(centre, FxDistortion.Roar);
        _visualsSystem.Shockwave(centre, strength: 3f);
        _visualsSystem.WaveAt(centre, FxColour.Blood, big: true);
        _visualsSystem.WaveAt(centre, FxColour.Gold, big: true);
        _audioSystem.PlayPvs(RoarSound, entity, audioParams: AudioParams.Default.WithVolume(8f).WithPitchScale(0.5f));
    }

    #endregion
}
