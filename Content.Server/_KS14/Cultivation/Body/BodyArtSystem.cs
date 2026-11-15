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
using Content.Shared.Throwing;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

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

    [Dependency] private IGameTiming _gameTiming = default!;
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
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TagSystem _tagSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
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
    }

    #region Helpers

    /// <summary>
    ///     Pay a body art's exhaustion, or say why not.
    /// </summary>
    private bool TryExert(Entity<BodyCultivatorComponent> entity, float exhaustion)
    {
        if (!_bodySystem.CanExert(entity.Comp, exhaustion))
        {
            _popupSystem.PopupEntity(Loc.GetString("body-art-too-exhausted",
                ("exhaustion", (int)entity.Comp.Exhaustion), ("cost", (int)exhaustion)), entity, entity, PopupType.SmallCaution);
            return false;
        }

        _bodySystem.AddExhaustion(entity.AsNullable(), exhaustion);
        return true;
    }

    /// <summary>
    ///     Wuxia heroes announce their moves.
    /// </summary>
    private void Shout(EntityUid user, string artLoc)
    {
        _chatSystem.TrySendInGameICMessage(user, Loc.GetString(artLoc).ToUpperInvariant() + "!!", InGameICChatType.Speak,
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
    public bool Hit(EntityUid user, EntityUid victim, float damage, TimeSpan knockdown)
    {
        if (victim == user || !_mobStateQuery.TryComp(victim, out var mobStateComponent) || _mobStateSystem.IsDead(victim, mobStateComponent))
            return false;

        if (PathPower(victim) > PathPower(user))
        {
            _damageableSystem.TryChangeDamage(victim, CultivationSystem.Damage("Blunt", damage / 2f), origin: user);
            return false;
        }

        _damageableSystem.TryChangeDamage(victim, CultivationSystem.Damage("Blunt", damage), origin: user);
        if (knockdown > TimeSpan.Zero)
            _stunSystem.TryKnockdown(victim, knockdown);

        return true;
    }

    private void HurlFrom(EntityUid victim, Vector2 origin, float distance, EntityUid user)
    {
        var direction = _transformSystem.GetMapCoordinates(victim).Position - origin;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        _throwingSystem.TryThrow(victim, Vector2.Normalize(direction) * distance, baseThrowSpeed: 10f, user: user, recoil: false);
    }

    private List<EntityUid> MobsAround(MapCoordinates centre, float radius, EntityUid exclude)
    {
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, radius, _mobs);
        var found = new List<EntityUid>();
        foreach (var mob in _mobs)
        {
            if (mob.Owner != exclude)
                found.Add(mob.Owner);
        }

        return found;
    }

    /// <summary>
    ///     What walls a stage can break: 0 none, 1 plain walls, 2 reinforced walls too.
    /// </summary>
    public static int WallTier(int stage)
    {
        return stage >= 8 ? 2 : (stage >= 6 ? 1 : 0);
    }

    /// <summary>
    ///     Smash everything breakable around a point: windows, grilles, tables and machines always; walls by tier.
    /// </summary>
    public void Smash(MapCoordinates centre, float radius, float power, int wallTier, EntityUid user)
    {
        _breakables.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, radius, _breakables, LookupFlags.Static | LookupFlags.Dynamic);
        foreach (var breakable in _breakables)
        {
            if (breakable.Owner == user || _mobStateQuery.HasComp(breakable.Owner))
                continue;

            var damage = power;
            if (_tagSystem.HasTag(breakable.Owner, WallTag))
            {
                // Plain walls give way to the first tier; reinforced ones are built to take several times as much.
                if (wallTier <= 0)
                    continue;

                damage = wallTier >= 2 ? 1500f : 400f;
            }
            else if (_tagSystem.HasTag(breakable.Owner, WindowTag))
            {
                damage = MathF.Max(power, 150f);
            }

            _damageableSystem.TryChangeDamage(breakable.Owner, CultivationSystem.Damage("Structural", damage), origin: user);
        }
    }

    /// <summary>
    ///     A shockwave tearing down a line: everyone in it is hit and hurled along it, and it smashes what it passes.
    /// </summary>
    private void ShatterLine(EntityUid user, Vector2 origin, Vector2 direction, int length, int halfWidth, float damage, int wallTier)
    {
        var mapId = _transformSystem.GetMapId(user);
        direction = Vector2.Normalize(direction);
        var side = new Vector2(-direction.Y, direction.X);
        var hit = new HashSet<EntityUid>();

        for (var step = 1; step <= length; step++)
        {
            for (var offset = -halfWidth; offset <= halfWidth; offset++)
            {
                var point = new MapCoordinates(origin + direction * step + side * offset, mapId);
                Smash(point, 0.5f, 120f, wallTier, user);
                foreach (var victim in MobsAround(point, 0.6f, user))
                {
                    if (!hit.Add(victim))
                        continue;

                    if (Hit(user, victim, damage, TimeSpan.FromSeconds(2)))
                        _throwingSystem.TryThrow(victim, direction * 3f, baseThrowSpeed: 10f, user: user, recoil: false);
                }
            }
        }
    }

    private Vector2 Towards(EntityUid user, EntityCoordinates target, out float distance)
    {
        var offset = _transformSystem.ToMapCoordinates(target).Position - _transformSystem.GetMapCoordinates(user).Position;
        distance = offset.Length();
        return distance > 0.0001f ? offset / distance : new Vector2(0f, 1f);
    }

    /// <summary>
    ///     Throw someone (often oneself) through the air to land with a body art's force.
    /// </summary>
    private void Launch(EntityUid thrown, Vector2 offset, float speed, EntityUid source, BodyLandingComponent landing)
    {
        var landingComponent = EnsureComp<BodyLandingComponent>(thrown);
        landingComponent.Source = source;
        landingComponent.Radius = landing.Radius;
        landingComponent.Damage = landing.Damage;
        landingComponent.CentreDamage = landing.CentreDamage;
        landingComponent.Knockdown = landing.Knockdown;
        landingComponent.Smash = landing.Smash;
        landingComponent.HurtSelf = landing.HurtSelf;
        landingComponent.ArtName = landing.ArtName;
        _throwingSystem.TryThrow(thrown, offset, baseThrowSpeed: speed, user: source == thrown ? null : source, recoil: false, doSpin: thrown != source);
    }

    [SubscribeLocalEvent]
    private void OnLand(Entity<BodyLandingComponent> entity, ref LandEvent args)
    {
        var landing = entity.Comp;
        RemCompDeferred(entity, landing);
        var source = landing.Source ?? entity.Owner;
        var centre = _transformSystem.GetMapCoordinates(entity);

        if (landing.HurtSelf)
        {
            Hit(source, entity, landing.Damage, landing.Knockdown);
        }
        else if (landing.Radius > 0f)
        {
            foreach (var victim in MobsAround(centre, landing.Radius, entity))
            {
                var near = _transformSystem.InRange((victim, null), (entity.Owner, null), 0.75f);
                Hit(source, victim, near ? landing.Damage + landing.CentreDamage : landing.Damage, landing.Knockdown);
            }
        }

        if (landing.Smash > 0f)
            Smash(centre, MathF.Max(landing.Radius, 1f), landing.Smash, 0, source);

        if (landing.Radius > 0f || landing.HurtSelf)
            _audioSystem.PlayPvs(ImpactSound, entity);
    }

    #endregion

    #region Stage 1-2: Breath of Renewal, Iron Shirt, Mountain Leap, Earth-Shattering Stomp

    [SubscribeLocalEvent]
    private void OnBreathOfRenewal(Entity<BodyCultivatorComponent> entity, ref BodyBreathOfRenewalActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var breathing = EnsureComp<BodyBreathingComponent>(entity);
        breathing.BreathsLeft = 2;
        breathing.ExhaustionPerBreath = args.Exhaustion;
        StartBreath(entity);
    }

    private void StartBreath(EntityUid user)
    {
        var doAfterArgs = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(2), new BodyBreathDoAfterEvent(), user)
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
        if (args.Handled || !TryComp<BodyBreathingComponent>(entity, out var breathing))
            return;

        args.Handled = true;
        if (args.Cancelled)
        {
            RemComp<BodyBreathingComponent>(entity);
            return;
        }

        var heal = 5f + 2f * entity.Comp.Stage + SharedBodyCultivationSystem.GetLevel(entity.Comp, BodyGroup.Heart);
        _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-heal * 2f));
        _audioSystem.PlayPvs(BreathSound, entity);

        // Up to three breaths; the first was paid for by casting, the rest cost their own.
        if (breathing.BreathsLeft <= 0 || !_bodySystem.CanExert(entity.Comp, breathing.ExhaustionPerBreath))
        {
            RemComp<BodyBreathingComponent>(entity);
            return;
        }

        breathing.BreathsLeft--;
        _bodySystem.AddExhaustion(entity.AsNullable(), breathing.ExhaustionPerBreath);
        args.Repeat = true;
    }

    [SubscribeLocalEvent]
    private void OnIronShirt(Entity<BodyCultivatorComponent> entity, ref BodyIronShirtActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var duration = TimeSpan.FromSeconds(10 + entity.Comp.Stage);
        if (_bodySystem.GetStance(entity) == BodyStance.Mountain)
            duration *= 1.5;

        _bodySystem.ApplyIronShirt(entity, duration, 0.5f);
        Shout(entity, "body-art-iron-shirt");
        _popupSystem.PopupEntity(Loc.GetString("body-art-iron-shirt-on", ("user", entity.Owner)), entity, PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnMountainLeap(Entity<BodyCultivatorComponent> entity, ref BodyMountainLeapActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        var direction = Towards(entity, args.Target, out var distance);
        distance = MathF.Min(distance, 3 + stage);

        // From Crimson Blood the landing cracks the ground and floors everyone around.
        var landing = new BodyLandingComponent();
        if (stage >= 5)
        {
            landing.Radius = 1 + (stage - 5) / 2;
            landing.Damage = 2 + stage;
            landing.Knockdown = TimeSpan.FromSeconds(1 + stage * 0.1);
            landing.Smash = stage >= 7 ? 60 + 10 * stage : 0;
        }

        Launch(entity, direction * distance, 8f + stage, entity, landing);
    }

    [SubscribeLocalEvent]
    private void OnEarthStomp(Entity<BodyCultivatorComponent> entity, ref BodyEarthStompActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        var centre = _transformSystem.GetMapCoordinates(entity);
        var radius = 1.5f + stage / 3f;
        var mountain = _bodySystem.GetStance(entity) == BodyStance.Mountain;

        Shout(entity, "body-art-earth-stomp");
        foreach (var victim in MobsAround(centre, radius, entity))
        {
            if (Hit(entity, victim, 5 + stage, TimeSpan.FromSeconds(1 + 0.2 * stage)) && mountain)
                HurlFrom(victim, centre.Position, 2f + stage / 3f, entity);
        }

        if (stage >= 6)
            Smash(centre, radius, 40 + 10 * stage, 0, entity);

        _audioSystem.PlayPvs(RumbleSound, entity);
    }

    #endregion

    #region Stage 3: Shattering Fist, Hundred Fist Barrage, Bone Setting, stances, disciples

    [SubscribeLocalEvent]
    private void OnShatteringFist(Entity<BodyCultivatorComponent> entity, ref BodyShatteringFistActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        var arms = SharedBodyCultivationSystem.GetLevel(entity.Comp, BodyGroup.Arms);
        var origin = _transformSystem.GetMapCoordinates(entity).Position;
        var direction = _transformSystem.GetMapCoordinates(args.Target).Position - origin;
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        Shout(entity, "body-art-shattering-fist");
        _audioSystem.PlayPvs(PunchSound, args.Target);

        if (_mobStateQuery.HasComp(args.Target))
        {
            if (Hit(entity, args.Target, 10 + 2 * arms + 2 * stage, TimeSpan.FromSeconds(1 + 0.2 * stage)))
                _throwingSystem.TryThrow(args.Target, Vector2.Normalize(direction) * (3 + stage / 2), baseThrowSpeed: 12f, user: entity, recoil: false);

            if (stage >= 7)
                ShatterLine(entity, _transformSystem.GetMapCoordinates(args.Target).Position, direction, stage - 4, stage >= 9 ? 1 : 0, 8 + stage, WallTier(stage));
            return;
        }

        // At a wall, door or machine: from Jade Marrow the punch blasts a trench through it and what's behind it.
        var fistTier = stage >= 7 ? 2 : (stage >= 4 ? 1 : 0);
        Smash(_transformSystem.GetMapCoordinates(args.Target), 0.5f, 60 + 20 * stage, fistTier, entity);
        if (stage >= 4)
        {
            var depth = stage >= 9 ? 9 : (stage >= 8 ? 6 : (stage >= 7 ? 4 : stage - 3));
            ShatterLine(entity, origin, direction, depth, stage >= 9 ? 2 : (stage >= 7 ? 1 : 0), 8 + stage, fistTier);
        }
    }

    [SubscribeLocalEvent]
    private void OnHundredFists(Entity<BodyCultivatorComponent> entity, ref BodyHundredFistsActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        var blows = 3 + stage;
        var perBlow = 3f + stage / 2f + entity.Comp.PunchDamage / 2f;
        if (_bodySystem.GetStance(entity) == BodyStance.Thunderclap)
            perBlow += 1f;

        Shout(entity, "body-art-hundred-fists");
        for (var blow = 0; blow < blows - 1; blow++)
            Hit(entity, args.Target, perBlow, TimeSpan.Zero);

        // The last one sends them flying.
        if (Hit(entity, args.Target, perBlow * 2f, TimeSpan.FromSeconds(2)))
            HurlFrom(args.Target, _transformSystem.GetMapCoordinates(entity).Position, 3f, entity);

        _audioSystem.PlayPvs(PunchSound, args.Target);
        _popupSystem.PopupEntity(Loc.GetString("body-art-hundred-fists-hit", ("user", entity.Owner), ("target", args.Target), ("blows", blows)),
            args.Target, PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnBoneSetting(Entity<BodyCultivatorComponent> entity, ref BodyBoneSettingActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        // Wrench dislocations and fractures back into place: it heals blunt trauma above all.
        var heal = 10f + 3f * entity.Comp.Stage;
        _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Blunt", -heal), ignoreResistances: true, interruptsDoAfters: false);
        _popupSystem.PopupEntity(Loc.GetString("body-art-bone-setting", ("user", entity.Owner)), entity, PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnStance(Entity<BodyCultivatorComponent> entity, ref BodyStanceActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_bodySystem.GetStance(entity) == args.Stance)
        {
            _bodySystem.SetStance(entity, null);
            _popupSystem.PopupEntity(Loc.GetString("body-stance-relax"), entity, entity);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, entity, TimeSpan.FromSeconds(2), new BodyStanceDoAfterEvent(args.Stance), entity)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _popupSystem.PopupEntity(Loc.GetString("body-stance-settling", ("user", entity.Owner)), entity, PopupType.Small);
    }

    [SubscribeLocalEvent]
    private void OnStanceDoAfter(Entity<BodyCultivatorComponent> entity, ref BodyStanceDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        _bodySystem.SetStance(entity, args.Stance);
        _popupSystem.PopupEntity(Loc.GetString($"body-stance-{args.Stance}"), entity, entity, PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnDisciple(Entity<BodyCultivatorComponent> entity, ref BodyDiscipleActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var disciple = args.Target;

        // Only someone who has already set foot on the path (read the primer) can be taken on.
        if (!_bodyCultivatorQuery.TryComp(disciple, out var discipleComponent) || _cultivatorQuery.HasComp(disciple))
        {
            _popupSystem.PopupEntity(Loc.GetString("body-disciple-not-on-path", ("target", disciple)), entity, entity);
            return;
        }

        if (discipleComponent.Committed)
        {
            _popupSystem.PopupEntity(Loc.GetString("body-disciple-already", ("target", disciple)), entity, entity);
            return;
        }

        _bodySystem.Commit((disciple, discipleComponent));
        _bodySystem.AddTempering((disciple, discipleComponent), 30f);
        _popupSystem.PopupEntity(Loc.GetString("body-disciple-accepted", ("user", entity.Owner), ("target", disciple)), disciple, PopupType.Large);
    }

    #endregion

    #region Stage 4-5: Remold Flesh, Raging Bull Charge, Blood Boil, Falling Mountain Descent, Mountain-Toppling Throw

    [SubscribeLocalEvent]
    private void OnRemold(Entity<BodyCultivatorComponent> entity, ref BodyRemoldActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        // The Body Molding Art itself: flesh is clay to be shaped again.
        _damageableSystem.HealEvenly(entity.Owner, FixedPoint2.New(-(30f + 5f * entity.Comp.Stage)));
        _popupSystem.PopupEntity(Loc.GetString("body-art-remold", ("user", entity.Owner)), entity, PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnBullCharge(Entity<BodyCultivatorComponent> entity, ref BodyBullChargeActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        var origin = _transformSystem.GetMapCoordinates(entity);
        var direction = Towards(entity, args.Target, out _);
        var length = 4 + stage;

        Shout(entity, "body-art-bull-charge");

        // Bowl everyone in the path aside and burst through what's in the way, then follow through.
        var bowled = new HashSet<EntityUid>();
        for (var step = 1; step <= length; step++)
        {
            var point = new MapCoordinates(origin.Position + direction * step, origin.MapId);
            Smash(point, 0.5f, 80 + 10 * stage, stage >= 6 ? 1 : 0, entity);
            foreach (var victim in MobsAround(point, 0.8f, entity))
            {
                if (!bowled.Add(victim))
                    continue;

                if (Hit(entity, victim, 8 + stage, TimeSpan.FromSeconds(1.5)))
                    _throwingSystem.TryThrow(victim, new Vector2(-direction.Y, direction.X) * 2f, baseThrowSpeed: 10f, user: entity, recoil: false);
            }
        }

        _throwingSystem.TryThrow(entity, direction * length, baseThrowSpeed: 10f + stage, recoil: false, doSpin: false);
        _audioSystem.PlayPvs(RumbleSound, entity);
    }

    [SubscribeLocalEvent]
    private void OnBloodBoil(Entity<BodyCultivatorComponent> entity, ref BodyBloodBoilActionEvent args)
    {
        if (args.Handled || _bodySystem.InBloodBoil(entity))
            return;

        args.Handled = true;
        _bodySystem.ApplyBloodBoil(entity, TimeSpan.FromSeconds(15), args.CrashExhaustion);
        Shout(entity, "body-art-blood-boil");
        _popupSystem.PopupEntity(Loc.GetString("body-art-blood-boil-on", ("user", entity.Owner)), entity, PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnBloodBoilEnded(Entity<BodyCultivatorComponent> entity, ref BodyBloodBoilEndedEvent args)
    {
        _popupSystem.PopupEntity(Loc.GetString("body-art-blood-boil-crash"), entity, entity, PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnFallingMountain(Entity<BodyCultivatorComponent> entity, ref BodyFallingMountainActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        var direction = Towards(entity, args.Target, out var distance);
        distance = MathF.Min(distance, 9f);

        Shout(entity, "body-art-falling-mountain");
        Launch(entity, direction * distance, 10f, entity, new BodyLandingComponent
        {
            Radius = 2f,
            Damage = 10 + stage,
            CentreDamage = 10 + 2 * stage,
            Knockdown = TimeSpan.FromSeconds(2),
            Smash = 80 + 10 * stage,
        });
    }

    [SubscribeLocalEvent]
    private void OnMountainHurl(Entity<BodyCultivatorComponent> entity, ref BodyMountainHurlActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<PullerComponent>(entity, out var pullerComponent) || pullerComponent.Pulling is not { } victim
            || !_mobStateQuery.HasComp(victim))
        {
            _popupSystem.PopupEntity(Loc.GetString("body-art-hurl-needs-grab"), entity, entity);
            return;
        }

        if (!TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        if (TryComp<PullableComponent>(victim, out var pullableComponent))
            _pullingSystem.TryStopPull(victim, pullableComponent, entity);

        var direction = Towards(entity, args.Target, out var distance);
        distance = MathF.Min(distance, 4 + stage);

        Shout(entity, "body-art-mountain-hurl");
        Launch(victim, direction * distance, 12f, entity, new BodyLandingComponent
        {
            HurtSelf = true,
            Damage = 10 + 2 * stage,
            Knockdown = TimeSpan.FromSeconds(2),
        });
    }

    #endregion

    #region Stage 6-9: Sky-Splitting Palm, Vajra Golden Body, Heaven-Shaking Quake, Primordial Roar

    [SubscribeLocalEvent]
    private void OnSkySplittingPalm(Entity<BodyCultivatorComponent> entity, ref BodySkySplittingPalmActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var stage = entity.Comp.Stage;
        var direction = Towards(entity, args.Target, out _);

        Shout(entity, "body-art-sky-splitting-palm");
        ShatterLine(entity, _transformSystem.GetMapCoordinates(entity).Position, direction, 7 + stage, stage >= 8 ? 1 : 0, 10 + 2 * stage, WallTier(stage));
        _audioSystem.PlayPvs(RumbleSound, entity);
    }

    [SubscribeLocalEvent]
    private void OnVajra(Entity<BodyCultivatorComponent> entity, ref BodyVajraActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var duration = TimeSpan.FromSeconds(8);
        if (_bodySystem.GetStance(entity) == BodyStance.Mountain)
            duration *= 1.5;

        _bodySystem.ApplyVajra(entity, duration);
        Shout(entity, "body-art-vajra");
        _audioSystem.PlayPvs(BellSound, entity);
        _popupSystem.PopupEntity(Loc.GetString("body-art-vajra-on", ("user", entity.Owner)), entity, PopupType.Large);
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
        quakeComponent.Radius = 4f + stage / 3f;
        quakeComponent.Damage = 4f + stage / 2f;
        quakeComponent.NextPulse = _gameTiming.CurTime;

        Shout(entity, "body-art-heaven-quake");
    }

    private void QuakePulse(EntityUid user, BodyQuakeComponent quakeComponent)
    {
        var centre = _transformSystem.GetMapCoordinates(user);
        foreach (var victim in MobsAround(centre, quakeComponent.Radius, user))
            Hit(user, victim, quakeComponent.Damage, TimeSpan.FromSeconds(1.5));

        // Windows rattle apart.
        _breakables.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, quakeComponent.Radius, _breakables, LookupFlags.Static);
        foreach (var breakable in _breakables)
        {
            if (_tagSystem.HasTag(breakable.Owner, WindowTag))
                _damageableSystem.TryChangeDamage(breakable.Owner, CultivationSystem.Damage("Structural", 40f), origin: user);
        }

        _audioSystem.PlayPvs(RumbleSound, user);
    }

    [SubscribeLocalEvent]
    private void OnPrimordialRoar(Entity<BodyCultivatorComponent> entity, ref BodyPrimordialRoarActionEvent args)
    {
        if (args.Handled || !TryExert(entity, args.Exhaustion))
            return;

        args.Handled = true;
        var centre = _transformSystem.GetMapCoordinates(entity);

        Shout(entity, "body-art-primordial-roar");
        foreach (var victim in MobsAround(centre, 7f, entity))
        {
            if (Hit(entity, victim, 15f, TimeSpan.FromSeconds(3)))
                _staminaSystem.TakeStaminaDamage(victim, 40f, source: entity);
        }

        // Every window shatters.
        _breakables.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, 7f, _breakables, LookupFlags.Static);
        foreach (var breakable in _breakables)
        {
            if (_tagSystem.HasTag(breakable.Owner, WindowTag))
                _damageableSystem.TryChangeDamage(breakable.Owner, CultivationSystem.Damage("Structural", 300f), origin: entity);
        }

        _audioSystem.PlayPvs(RoarSound, entity, AudioParams.Default.WithVolume(8f).WithPitchScale(0.5f));
    }

    #endregion
}
