using System.Numerics;
using Content.Server._KS14.Cultivation.Body;
using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared._KS14.Explosion.Shockwave;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The glowing face of cultivation. Bursts (qi waves, sparks, rising motes, a pillar of light, a rippling
///         shockwave) are spawned where techniques land, in their element's colour, and cast real coloured light.
///         Lasting states (Golden Bell, wards, stances, Iron Shirt, a duel, vengeance...) wear a glowing aura, dome or
///         sigil that follows the body for as long as the state does.
/// </summary>
public sealed partial class CultivationVisualsSystem : EntitySystem
{
    private static readonly EntProtoId ShockwaveEffect = "KsBodyShockwave";
    private static readonly EntProtoId PillarEffect = "KsFxAscendPillar";
    private static readonly EntProtoId StrikeFlashEffect = "KsFxStrikeFlash";
    private static readonly EntProtoId CrackleEffect = "KsFxCrackle";
    private static readonly EntProtoId CrackleAuraEffect = "KsFxCrackleAura";
    private static readonly EntProtoId[] HeavenBoltEffects = { "KsFxHeavenBolt0", "KsFxHeavenBolt1", "KsFxHeavenBolt2" };
    public static readonly SoundSpecifier ThunderclapSound = new SoundPathSpecifier("/Audio/Effects/Lightning/lightningbolt.ogg");
    private static readonly EntProtoId FlameEffect = "KsFxFlame";
    private static readonly EntProtoId FlameBigEffect = "KsFxFlameBig";
    private static readonly EntProtoId FlameSmallEffect = "KsFxFlameSmall";
    private static readonly EntProtoId WaterSprayEffect = "KsFxWaterSpray";
    private static readonly EntProtoId WaterPuffEffect = "KsFxWaterPuff";
    private static readonly EntProtoId WaterSwirlEffect = "KsFxWaterSwirl";
    public static readonly EntProtoId MistWaterEffect = "KsFxMistWater";
    public static readonly EntProtoId MistBloodEffect = "KsFxMistBloodReal";
    private static readonly EntProtoId LeafPuffEffect = "KsFxLeafPuff";
    private static readonly EntProtoId BlossomPuffEffect = "KsFxBlossomPuff";
    private static readonly EntProtoId PollenEffect = "KsFxPollen";
    private static readonly EntProtoId MetalSparkEffect = "KsFxMetalSpark";
    private static readonly EntProtoId RiftEffect = "KsFxVoidRift";
    private static readonly EntProtoId RubbleEffect = "KsFxRubble";
    private static readonly EntProtoId DustEffect = "KsBodyDust";
    private static readonly EntProtoId[] CraterEffects = { "KsFxCrater", "KsFxCraterBig", "KsFxCraterHuge" };

    // The SS13 sprites for lasting states.
    private static readonly EntProtoId GoldenBellEffect = "KsFxGoldenBell";
    private static readonly EntProtoId WaterBubbleEffect = "KsFxWaterBubble";
    private static readonly EntProtoId MirrorBubbleEffect = "KsFxMirrorBubble";
    private static readonly EntProtoId DharmaIdolEffect = "KsFxDharmaIdol";
    private static readonly EntProtoId StormCloudEffect = "KsFxStormCloud";
    private static readonly EntProtoId DragonAuraEffect = "KsFxDragonAura";
    private static readonly EntProtoId DragonAuraFrontEffect = "KsFxDragonAuraFront";
    private static readonly EntProtoId HeartDemonEffect = "KsFxHeartDemonAura";
    private static readonly EntProtoId BeastEffect = "KsFxBeastAura";
    private static readonly EntProtoId SwordRideEffect = "KsFxSwordRide";
    private static readonly EntProtoId SwordOrbitEffect = "KsFxSwordOrbit";

    // Each technique's own animation (wuxia_arts.yml).
    public static readonly EntProtoId ArtPalmQi = "KsFxArtPalmQi";
    public static readonly EntProtoId ArtPalmFire = "KsFxArtPalmFire";
    public static readonly EntProtoId ArtPalmBlood = "KsFxArtPalmBlood";
    public static readonly EntProtoId ArtPalmGold = "KsFxArtPalmGold";
    public static readonly EntProtoId ArtSwordRain = "KsFxArtSwordRain";
    public static readonly EntProtoId ArtSwordOrbit = "KsFxArtSwordOrbit";
    public static readonly EntProtoId ArtTalismanFlare = "KsFxArtTalismanFlare";
    public static readonly EntProtoId ArtEmbers = "KsFxArtEmbers";
    public static readonly EntProtoId ArtPetals = "KsFxArtPetals";
    public static readonly EntProtoId ArtPetalsBig = "KsFxArtPetalsBig";
    public static readonly EntProtoId ArtGoldMotes = "KsFxArtGoldMotes";
    public static readonly EntProtoId ArtVoidMotes = "KsFxArtVoidMotes";
    public static readonly EntProtoId ArtFistGold = "KsFxArtFistGold";
    public static readonly EntProtoId ArtFistEarth = "KsFxArtFistEarth";
    public static readonly EntProtoId ArtFistQi = "KsFxArtFistQi";
    public static readonly EntProtoId ArtBarrage = "KsFxArtBarrage";
    public static readonly EntProtoId ArtFireRing = "KsFxArtFireRing";
    public static readonly EntProtoId ArtFireRingBig = "KsFxArtFireRingBig";
    public static readonly EntProtoId ArtBloodVortex = "KsFxArtBloodVortex";
    public static readonly EntProtoId ArtRoarBlood = "KsFxArtRoarBlood";
    public static readonly EntProtoId ArtRoarGold = "KsFxArtRoarGold";
    public static readonly EntProtoId ArtRoarSense = "KsFxArtRoarSense";
    public static readonly EntProtoId ArtFissures = "KsFxArtFissures";
    public static readonly EntProtoId ArtFissuresBig = "KsFxArtFissuresBig";
    public static readonly EntProtoId ArtFallingStar = "KsFxArtFallingStar";
    public static readonly EntProtoId ArtSheenIron = "KsFxArtSheenIron";
    public static readonly EntProtoId ArtSheenGold = "KsFxArtSheenGold";
    public static readonly EntProtoId ArtMendWood = "KsFxArtMendWood";
    public static readonly EntProtoId ArtMendBone = "KsFxArtMendBone";
    public static readonly EntProtoId ArtMendWater = "KsFxArtMendWater";
    public static readonly EntProtoId ArtMistBlood = "KsFxArtMistBlood";
    public static readonly EntProtoId ArtMistSteam = "KsFxArtMistSteam";
    public static readonly EntProtoId ArtMistDust = "KsFxArtMistDust";
    public static readonly EntProtoId ArtMistShadow = "KsFxArtMistShadow";
    public static readonly EntProtoId QiCloud = "KsFxQiCloud";

    /// <summary>
    ///     The layered SS13 cues (temple bells, guqin phrases, chimes), mixed down into single files.
    /// </summary>
    public static readonly SoundSpecifier GreatBellSound = new SoundPathSpecifier("/Audio/_KS14/Cultivation/great_bell.ogg");
    public static readonly SoundSpecifier TempleBellSound = new SoundPathSpecifier("/Audio/_KS14/Cultivation/temple_bell.ogg");
    public static readonly SoundSpecifier GuqinRiseSound = new SoundPathSpecifier("/Audio/_KS14/Cultivation/guqin_rise.ogg");
    public static readonly SoundSpecifier GuqinFallSound = new SoundPathSpecifier("/Audio/_KS14/Cultivation/guqin_fall.ogg");
    public static readonly SoundSpecifier WindChimesSound = new SoundPathSpecifier("/Audio/_KS14/Cultivation/wind_chimes.ogg");
    public static readonly SoundSpecifier QinggongSound = new SoundPathSpecifier("/Audio/_KS14/Cultivation/qinggong.ogg");
    public static readonly SoundSpecifier MountainFallSound = new SoundPathSpecifier("/Audio/_KS14/Cultivation/mountain_fall.ogg");
    public static readonly SoundSpecifier AscendSound = new SoundPathSpecifier("/Audio/_KS14/Cultivation/ascend.ogg");

    /// <summary>
    ///     What each colour of qi sounds like as it's let loose.
    /// </summary>
    private static readonly Dictionary<FxColour, SoundSpecifier> Cues = new()
    {
        [FxColour.Qi] = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg"),
        [FxColour.Gold] = new SoundPathSpecifier("/Audio/Effects/holy.ogg"),
        [FxColour.Metal] = new SoundPathSpecifier("/Audio/Weapons/eblademiss.ogg"),
        [FxColour.Water] = new SoundPathSpecifier("/Audio/Effects/waterswirl.ogg"),
        [FxColour.Fire] = new SoundPathSpecifier("/Audio/Magic/fireball.ogg"),
        [FxColour.Earth] = new SoundPathSpecifier("/Audio/Effects/break_stone.ogg"),
        [FxColour.Wood] = new SoundPathSpecifier("/Audio/Effects/plant_rustle.ogg"),
        [FxColour.Blood] = new SoundPathSpecifier("/Audio/Effects/demon_consume.ogg"),
        [FxColour.Void] = new SoundPathSpecifier("/Audio/Magic/blink.ogg"),
        [FxColour.Sense] = new SoundPathSpecifier("/Audio/Effects/chime.ogg"),
    };

    private static readonly TimeSpan AuraInterval = TimeSpan.FromSeconds(0.25);

    [Dependency] private Content.Shared.Standing.StandingStateSystem _standingStateSystem = default!;
    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobStateSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<BodyCultivatorComponent> _bodyCultivatorQuery = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;

    /// <summary>
    ///     Which lasting state wears which glow. A state is "on" while its component is present and, where given,
    ///         the check passes.
    /// </summary>
    private readonly List<AuraRule> _auraRules = new();
    private readonly Dictionary<(EntityUid Owner, int Rule), EntityUid> _auras = new();
    private readonly HashSet<(EntityUid Owner, int Rule)> _aurasSeen = new();
    private readonly List<(EntityUid Owner, int Rule)> _aurasGone = new();
    private TimeSpan _nextAuraCheck;

    private readonly record struct AuraRule(Type Component, EntProtoId Effect, Func<IComponent, bool>? Active = null);

    public override void Initialize()
    {
        base.Initialize();

        // Qi.
        AddAura<GoldenBellQiComponent>(GoldenBellEffect);
        AddAura<StillWaterWardComponent>(WaterBubbleEffect);
        AddAura<RootedStanceComponent>("Sigil", FxColour.Earth);
        AddAura<DharmaIdolComponent>(DharmaIdolEffect);
        AddAura<TurtleBreathingComponent>(WaterBubbleEffect);
        AddAura<MirrorLakeComponent>(MirrorBubbleEffect);
        AddAura<MoltenStepComponent>("Aura", FxColour.Fire);
        AddAura<SwordRidingComponent>(SwordRideEffect);
        AddAura<SwordFormationComponent>(SwordOrbitEffect);
        AddAura<CultivationHasteComponent>("Aura", FxColour.Qi);
        AddAura<SoulDepartedComponent>("Aura", FxColour.Void);
        AddAura<NascentProjectionComponent>("Aura", FxColour.Gold);
        AddAura<MandateOfHeavenComponent>("Aura", FxColour.Gold);
        AddAura<SonOfHeavenComponent>(DragonAuraEffect);
        AddAura<SonOfHeavenComponent>(DragonAuraFrontEffect);
        AddAura<CultivatorComponent>(StormCloudEffect, component => ((CultivatorComponent)component).BreakthroughCyclesLeft > 0);

        // Martial arts and the jianghu.
        AddAura<GoldenBellComponent>(GoldenBellEffect);
        AddAura<GracefulStepsComponent>("Aura", FxColour.Qi);
        AddAura<DuguCounterComponent>("Aura", FxColour.Metal);
        AddAura<GreatShiftComponent>("Aura", FxColour.Void);
        AddAura<GreatSageComponent>("Aura", FxColour.Gold);
        AddAura<HonorDuelComponent>("Sigil", FxColour.Blood);
        AddAura<SwornVengeanceComponent>("Aura", FxColour.Blood);

        // The Demonic Path.
        AddAura<ExplodingHeartComponent>("Aura", FxColour.Blood);
        AddAura<JiangshiComponent>("Aura", FxColour.Void);

        // The body.
        AddAura<BodyHardeningComponent>("Aura", FxColour.Earth, component => ((BodyHardeningComponent)component).IronShirtEnd > _gameTiming.CurTime);
        AddAura<BodyHardeningComponent>(GoldenBellEffect, component => ((BodyHardeningComponent)component).VajraEnd > _gameTiming.CurTime);
        AddAura<BodyHardeningComponent>("Aura", FxColour.Blood, component => ((BodyHardeningComponent)component).BloodBoilEnd > _gameTiming.CurTime);
        AddAura<BodyStanceComponent>("Aura", FxColour.Earth, component => ((BodyStanceComponent)component).Stance == BodyStance.Mountain);
        AddAura<BodyStanceComponent>(CrackleAuraEffect, component => ((BodyStanceComponent)component).Stance == BodyStance.Thunderclap);
        AddAura<BodyBreathingComponent>("Aura", FxColour.Wood);
        AddAura<BodyCultivatorComponent>(StormCloudEffect, component => ((BodyCultivatorComponent)component).TribulationCyclesLeft > 0);
        AddAura<HeartDemonComponent>(HeartDemonEffect);
        AddAura<SpiritBeastComponent>(BeastEffect);
    }

    private void AddAura<T>(string kind, FxColour colour, Func<IComponent, bool>? active = null) where T : IComponent
    {
        AddAura<T>(EffectId(kind, colour), active);
    }

    private void AddAura<T>(EntProtoId effect, Func<IComponent, bool>? active = null) where T : IComponent
    {
        _auraRules.Add(new AuraRule(typeof(T), effect, active));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        if (now < _nextAuraCheck)
            return;

        _nextAuraCheck = now + AuraInterval;
        _aurasSeen.Clear();
        for (var ruleIndex = 0; ruleIndex < _auraRules.Count; ruleIndex++)
        {
            var rule = _auraRules[ruleIndex];
            foreach (var (ownerUid, component) in EntityManager.GetAllComponents(rule.Component))
            {
                if (component.LifeStage > ComponentLifeStage.Running || rule.Active != null && !rule.Active(component))
                    continue;

                // Nobody's aura shows while they're down: lying across the floor it would only draw as bars over them.
                if (_standingStateSystem.IsDown(ownerUid) || !_mobStateSystem.IsAlive(ownerUid) && HasComp<Content.Shared.Mobs.Components.MobStateComponent>(ownerUid))
                    continue;

                var key = (ownerUid, ruleIndex);
                _aurasSeen.Add(key);
                if (_auras.TryGetValue(key, out var auraUid) && !TerminatingOrDeleted(auraUid))
                    continue;

                _auras[key] = SpawnAttachedTo(rule.Effect, new EntityCoordinates(ownerUid, Vector2.Zero));
            }
        }

        _aurasGone.Clear();
        foreach (var (key, auraUid) in _auras)
        {
            if (_aurasSeen.Contains(key))
                continue;

            QueueDel(auraUid);
            _aurasGone.Add(key);
        }

        foreach (var key in _aurasGone)
            _auras.Remove(key);
    }

    #region Bursts

    /// <summary>
    ///     A cracked crater punched into the floor, lingering for half a minute: size 0, 1 or 2.
    /// </summary>
    public EntityUid Crater(MapCoordinates coordinates, int size)
    {
        var craterUid = Spawn(CraterEffects[Math.Clamp(size, 0, CraterEffects.Length - 1)], coordinates);
        _transformSystem.SetLocalRotation(craterUid, Angle.FromDegrees(90 * _robustRandom.Next(4)));
        Spawn(RubbleEffect, coordinates);
        return craterUid;
    }

    /// <summary>
    ///     A technique's own animation (the KsFxArt prototypes), played once where it lands.
    /// </summary>
    public void Art(EntProtoId effect, MapCoordinates coordinates)
    {
        Spawn(effect, coordinates);
    }

    public void Art(EntProtoId effect, EntityCoordinates coordinates)
    {
        Spawn(effect, _transformSystem.ToMapCoordinates(coordinates));
    }

    /// <summary>
    ///     A technique's own animation, played once where someone stands, staying put as they move.
    /// </summary>
    public void Art(EntProtoId effect, EntityUid uid)
    {
        if (!TerminatingOrDeleted(uid))
            Spawn(effect, _transformSystem.GetMapCoordinates(uid));
    }

    /// <summary>
    ///     A technique's own animation, played once on someone and moving with them.
    /// </summary>
    public void ArtOn(EntProtoId effect, EntityUid uid)
    {
        if (!TerminatingOrDeleted(uid))
            SpawnAttachedTo(effect, new EntityCoordinates(uid, Vector2.Zero));
    }

    /// <summary>
    ///     Rock chips and a puff of dust, where something gave way.
    /// </summary>
    public void Rubble(MapCoordinates coordinates)
    {
        Spawn(RubbleEffect, coordinates);
        Spawn(DustEffect, coordinates);
    }

    /// <summary>
    ///     A rift in space snapping open and pinching shut, as someone steps through the void.
    /// </summary>
    public void Rift(MapCoordinates coordinates)
    {
        Spawn(RiftEffect, coordinates);
    }

    /// <summary>
    ///     One of the mixed-down SS13 cues.
    /// </summary>
    public void Play(SoundSpecifier sound, EntityUid source, float volume = 0f)
    {
        _audioSystem.PlayPvs(sound, source, AudioParams.Default.WithVolume(volume));
    }

    public void Play(SoundSpecifier sound, MapCoordinates coordinates, float volume = 0f)
    {
        _audioSystem.PlayPvs(sound, _transformSystem.ToCoordinates(coordinates), AudioParams.Default.WithVolume(volume));
    }

    /// <summary>
    ///     A sound meant to be heard well beyond the room: louder, and carried much further than normal.
    /// </summary>
    public void Boom(SoundSpecifier sound, MapCoordinates coordinates, float volume = 8f, float range = 32f)
    {
        _audioSystem.PlayPvs(sound, _transformSystem.ToCoordinates(coordinates),
            AudioParams.Default.WithVolume(volume).WithMaxDistance(range).WithRolloffFactor(0.5f));
    }

    public static string EffectId(string kind, FxColour colour)
    {
        return $"KsFx{kind}{colour}";
    }

    /// <summary>
    ///     How far along someone is, from 0 to 3: a qi cultivator's realm, or a third of a body cultivator's stage.
    /// </summary>
    public int Power(EntityUid uid)
    {
        if (_cultivatorQuery.TryComp(uid, out var cultivatorComponent))
            return (int)cultivatorComponent.Realm;

        return _bodyCultivatorQuery.TryComp(uid, out var bodyComponent) ? Math.Min(3, bodyComponent.Stage / 3) : 0;
    }

    /// <summary>
    ///     The show of letting a technique loose, greater the further along the caster is: a ring of qi and its
    ///         sound for anyone; rising motes from the second step; a great wave from the third; and at the peak the
    ///         air itself ripples.
    /// </summary>
    public void Cast(EntityUid uid, FxColour colour)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var power = Power(uid);
        var centre = _transformSystem.GetMapCoordinates(uid);
        Spawn($"KsFxCast{(power >= 2 ? "Big" : string.Empty)}{colour}", centre);
        if (Cues.TryGetValue(colour, out var sound))
            _audioSystem.PlayPvs(sound, uid, AudioParams.Default.WithVolume(-9f + 3f * (float)power).WithVariation(0.15f));

        if (power >= 1)
            Motes(uid, colour, TimeSpan.FromSeconds(0.8 + 0.4 * power));

        if (power >= 3)
            Shockwave(centre, strength: 1.2f);
    }

    public void Cast(EntityUid uid)
    {
        Cast(uid, ColourOf(uid));
    }

    /// <summary>
    ///     A technique landing on someone: a spark, and from a strong enough hand a ring of qi breaking on them.
    /// </summary>
    public void Strike(EntityUid userUid, EntityUid targetUid, FxColour colour)
    {
        Spark(targetUid, colour);
        if (Power(userUid) >= 2)
            Wave(targetUid, colour);
    }

    /// <summary>
    ///     A ring of qi bursting out from someone or something.
    /// </summary>
    public void Wave(EntityUid atUid, FxColour colour, bool big = false)
    {
        if (TerminatingOrDeleted(atUid))
            return;

        WaveAt(_transformSystem.GetMapCoordinates(atUid), colour, big);
        Cue(atUid, colour, loud: big);
    }

    /// <summary>
    ///     The sound of a colour of qi, a little different every time.
    /// </summary>
    public void Cue(EntityUid atUid, FxColour colour, bool loud = false)
    {
        if (Cues.TryGetValue(colour, out var sound))
            _audioSystem.PlayPvs(sound, atUid, AudioParams.Default.WithVolume(loud ? -2f : -8f).WithVariation(0.15f));
    }

    /// <summary>
    ///     Tongues of flame licking up around a point for a couple of seconds. Only light and show: what burns is
    ///         up to the technique.
    /// </summary>
    public void Flames(MapCoordinates centre, float radius, int count)
    {
        for (var flame = 0; flame < count; flame++)
        {
            var offset = radius <= 0f ? Vector2.Zero : _robustRandom.NextAngle().ToVec() * _robustRandom.NextFloat(radius * 0.3f, radius);
            Spawn(FlameEffect, centre.Offset(offset));
        }
    }

    public void Flames(EntityUid atUid, float radius, int count)
    {
        if (!TerminatingOrDeleted(atUid))
            Flames(_transformSystem.GetMapCoordinates(atUid), radius, count);
    }

    /// <summary>
    ///     Water bursting over someone: spray flung out in every direction, droplets, and a little mist.
    /// </summary>
    public void WaterSplash(EntityUid atUid, int count = 4)
    {
        if (TerminatingOrDeleted(atUid))
            return;

        var centre = _transformSystem.GetMapCoordinates(atUid);
        Spawn(MistWaterEffect, centre);
        Spawn(WaterSwirlEffect, centre);
        for (var drop = 0; drop < count; drop++)
        {
            var direction = _robustRandom.NextAngle().ToVec();
            Spray(WaterSprayEffect, centre.Offset(direction * 0.35f), direction);
            Spawn(WaterPuffEffect, centre.Offset(_robustRandom.NextVector2(0.6f)));
        }
    }

    /// <summary>
    ///     A wall of water crashing outward from someone in a ring, with mist rising behind it.
    /// </summary>
    public void WaterRing(EntityUid atUid, float radius, int count)
    {
        if (TerminatingOrDeleted(atUid))
            return;

        var centre = _transformSystem.GetMapCoordinates(atUid);
        var start = _robustRandom.NextFloat(0f, MathF.Tau);
        for (var wave = 0; wave < count; wave++)
        {
            var angle = start + wave * MathF.Tau / count;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            Spray(WaterSprayEffect, centre.Offset(direction * radius * 0.45f), direction);
            Spray(WaterSprayEffect, centre.Offset(direction * radius * 0.85f), direction);
            Spawn(WaterPuffEffect, centre.Offset(direction * radius * _robustRandom.NextFloat(0.3f, 1f)));
        }

        for (var mist = 0; mist < 3; mist++)
            Spawn(MistWaterEffect, centre.Offset(_robustRandom.NextVector2(radius * 0.5f)));
    }

    /// <summary>
    ///     Green life welling up: puffs of leaves and drifting pollen.
    /// </summary>
    public void WoodBloom(EntityUid atUid, float radius, int count)
    {
        if (TerminatingOrDeleted(atUid))
            return;

        var centre = _transformSystem.GetMapCoordinates(atUid);
        for (var puff = 0; puff < count; puff++)
        {
            var offset = radius <= 0f ? _robustRandom.NextVector2(0.4f) : _robustRandom.NextAngle().ToVec() * _robustRandom.NextFloat(0.2f, radius);
            Spawn(_robustRandom.Prob(0.6f) ? LeafPuffEffect : BlossomPuffEffect, centre.Offset(offset));
        }

        Spawn(PollenEffect, centre);
        if (radius >= 1.5f)
        {
            for (var cloud = 0; cloud < 3; cloud++)
                Spawn(PollenEffect, centre.Offset(_robustRandom.NextAngle().ToVec() * radius * 0.6f));
        }
    }

    /// <summary>
    ///     Steel on steel: a shower of sparks.
    /// </summary>
    public void MetalSparks(EntityUid atUid, int count = 3)
    {
        if (TerminatingOrDeleted(atUid))
            return;

        var centre = _transformSystem.GetMapCoordinates(atUid);
        for (var spark = 0; spark < count; spark++)
            Spawn(MetalSparkEffect, centre.Offset(_robustRandom.NextVector2(0.45f)));
    }

    /// <summary>
    ///     Mist or smoke billowing over someone: steam, blood, dust or water.
    /// </summary>
    public void Mist(EntityUid atUid, EntProtoId mist)
    {
        if (!TerminatingOrDeleted(atUid))
            Spawn(mist, _transformSystem.GetMapCoordinates(atUid));
    }

    private void Spray(EntProtoId prototype, MapCoordinates at, Vector2 direction)
    {
        var sprayUid = Spawn(prototype, at);
        _transformSystem.SetWorldRotation(sprayUid, direction.ToWorldAngle());
    }

    /// <summary>
    ///     A burst of real fire on someone: a tall flame on them and smaller ones licking out around it.
    /// </summary>
    public void FireBurst(EntityUid atUid, int count = 4)
    {
        if (TerminatingOrDeleted(atUid))
            return;

        var centre = _transformSystem.GetMapCoordinates(atUid);
        Spawn(FlameBigEffect, centre);
        for (var flame = 0; flame < count; flame++)
            Spawn(_robustRandom.Prob(0.5f) ? FlameEffect : FlameSmallEffect, centre.Offset(_robustRandom.NextVector2(0.7f)));
    }

    /// <summary>
    ///     Real fire roaring out from someone in a ring: tall flames around the edge, smaller ones filling it in.
    /// </summary>
    public void FireRing(EntityUid atUid, float radius, int ringFlames, int innerFlames)
    {
        if (TerminatingOrDeleted(atUid))
            return;

        var centre = _transformSystem.GetMapCoordinates(atUid);
        var start = _robustRandom.NextFloat(0f, MathF.Tau);
        for (var flame = 0; flame < ringFlames; flame++)
        {
            var angle = start + flame * MathF.Tau / ringFlames;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * _robustRandom.NextFloat(0.85f, 1.05f);
            Spawn(FlameBigEffect, centre.Offset(offset));
        }

        for (var flame = 0; flame < innerFlames; flame++)
        {
            var offset = _robustRandom.NextAngle().ToVec() * _robustRandom.NextFloat(0.6f, radius * 0.85f);
            Spawn(_robustRandom.Prob(0.5f) ? FlameEffect : FlameSmallEffect, centre.Offset(offset));
        }
    }

    public void WaveAt(MapCoordinates centre, FxColour colour, bool big = false)
    {
        Spawn(EffectId(big ? "WaveBig" : "Wave", colour), centre);
    }

    /// <summary>
    ///     A star of light where a technique strikes.
    /// </summary>
    public void Spark(EntityUid targetUid, FxColour colour)
    {
        if (TerminatingOrDeleted(targetUid))
            return;

        var centre = _transformSystem.GetMapCoordinates(targetUid);
        Spawn(EffectId("Spark", colour), centre.Offset(_robustRandom.NextVector2(0.25f)));
    }

    /// <summary>
    ///     Specks of qi rising around someone for a while; they follow as the body moves.
    /// </summary>
    public void Motes(EntityUid uid, FxColour colour, TimeSpan duration)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var motesUid = SpawnAttachedTo(EffectId("Motes", colour), new EntityCoordinates(uid, Vector2.Zero));
        if (TryComp<TimedDespawnComponent>(motesUid, out var despawnComponent))
            despawnComponent.Lifetime = (float)duration.TotalSeconds;
    }

    /// <summary>
    ///     The air itself ripples out from the point.
    /// </summary>
    public void Shockwave(MapCoordinates centre, float strength)
    {
        var waveUid = Spawn(ShockwaveEffect, centre);
        var waveComponent = EnsureComp<KsShockwaveComponent>(waveUid);
        waveComponent.StartTime = _gameTiming.CurTime;
        waveComponent.Width = 0.6f + 0.3f * strength;
        waveComponent.FalloffPower = MathF.Max(10f, 40f - 8f * strength);
        Dirty(waveUid, waveComponent);
    }

    /// <summary>
    ///     Space itself bends around the point for a moment, the way it does round a singularity.
    /// </summary>
    public void Distort(MapCoordinates centre, FxDistortion kind)
    {
        Spawn($"KsFxDistortion{kind}", centre);
    }

    /// <summary>
    ///     A bolt of heaven's lightning falls on someone: the bolt, the floor flashing white, a thunderclap heard across
    ///         the station and lightning left crawling over them.
    /// </summary>
    /// <param name="variant">Which bolt; the later ones strike a moment after the first.</param>
    public void HeavenBolt(EntityUid uid, int variant = 0)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var centre = _transformSystem.GetMapCoordinates(uid);
        Spawn(HeavenBoltEffects[Math.Clamp(variant, 0, HeavenBoltEffects.Length - 1)], centre);
        if (variant == 0)
        {
            Spawn(StrikeFlashEffect, centre);
            SpawnAttachedTo(CrackleEffect, new EntityCoordinates(uid, Vector2.Zero));
            Shockwave(centre, strength: 1f);
            Boom(ThunderclapSound, centre, volume: 6f, range: 40f);
        }
    }

    /// <summary>
    ///     A successful breakthrough: heaven's lightning strikes (once more for every realm climbed), the floor flashes
    ///         white, a column of gold rises into the sky, a great golden wave rolls out and the air shakes.
    /// </summary>
    public void Breakthrough(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var centre = _transformSystem.GetMapCoordinates(uid);
        var bolts = 1;
        if (_cultivatorQuery.TryComp(uid, out var cultivatorComponent))
            bolts = Math.Clamp((int)cultivatorComponent.Realm, 1, HeavenBoltEffects.Length);
        else if (_bodyCultivatorQuery.TryComp(uid, out var bodyComponent))
            bolts = Math.Clamp(bodyComponent.Stage / 3, 1, HeavenBoltEffects.Length);

        for (var bolt = 0; bolt < bolts; bolt++)
            HeavenBolt(uid, bolt);

        Spawn(PillarEffect, centre);
        WaveAt(centre, FxColour.Gold, big: true);
        // Only a light ripple of the air: the lightning, the pillar and the golden wave carry the moment.
        Shockwave(centre, strength: 1f);
        Motes(uid, FxColour.Gold, TimeSpan.FromSeconds(3));
        Boom(AscendSound, centre, volume: 4f, range: 30f);
    }

    /// <summary>
    ///     The colour of someone's cultivation: their Root Law's element, earth for the body, plain qi otherwise.
    /// </summary>
    public FxColour ColourOf(EntityUid uid)
    {
        if (_cultivatorQuery.TryComp(uid, out var cultivatorComponent)
            && _sharedCultivationSystem.GetRootLaw(cultivatorComponent) is { } rootLaw
            && ProtoMan.TryIndex(rootLaw, out var law))
            return ColourOf(law.Element);

        return _bodyCultivatorQuery.HasComp(uid) ? FxColour.Earth : FxColour.Qi;
    }

    public static FxColour ColourOf(CultivationElement element)
    {
        return element switch
        {
            CultivationElement.Metal => FxColour.Metal,
            CultivationElement.Wood => FxColour.Wood,
            CultivationElement.Water => FxColour.Water,
            CultivationElement.Fire => FxColour.Fire,
            CultivationElement.Earth => FxColour.Earth,
            _ => FxColour.Qi,
        };
    }

    #endregion
}

/// <summary>
///     The colours cultivation glows in; each names a set of effect prototypes (KsFxWaveGold and so on).
/// </summary>
public enum FxDistortion : byte
{
    /// <summary>
    ///     A short outward shove of the picture, for a blow that splits the air.
    /// </summary>
    Push,

    /// <summary>
    ///     A quick inward tug, for the ground heaving.
    /// </summary>
    Pulse,

    /// <summary>
    ///     The whole screen thrown outward for over a second.
    /// </summary>
    Roar,
}

/// <summary>
///     The colours cultivation glows in; each names a set of effect prototypes (KsFxWaveGold and so on).
/// </summary>
public enum FxColour : byte
{
    Qi,
    Gold,
    Metal,
    Water,
    Fire,
    Earth,
    Wood,
    Blood,
    Void,
    Sense,
}
