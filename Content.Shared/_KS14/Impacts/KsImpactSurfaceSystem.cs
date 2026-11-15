using System.Numerics;
using Content.Shared._KS14.Particles;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Impacts;

/// <summary>
///     TGMC's bullet impacts (<c>/datum/element/debris</c>): a hit on a surface puffs smoke back out of it, a ballistic
///         one throws off debris of the surface's <see cref="KsImpactMaterialPrototype"/> and maybe ricochets, and a
///         ballistic hit on a mob that its armour mostly soaked plays an armour sound.
///
///     All of it is cosmetic and fired for every bullet, so it only ever plays on a client, for itself
///         (<see cref="PlayImpact"/>): from the hit it predicted, or from the server's relayed hit when it predicted
///         none. No entity is networked for it: a bullet's hit rides its existing relay, and a hitscan's is one small
///         event (<see cref="PlayImpactPredicted"/>), as its beam sprites are.
/// </summary>
public sealed partial class KsImpactSurfaceSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedStorageSystem _storageSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private KsSharedParticlesSystem _particlesSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;

    [Dependency] private EntityQuery<KsImpactSurfaceComponent> _surfaceQuery = default!;
    [Dependency] private EntityQuery<KsImpactSurfaceStorageFillComponent> _storageFillQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;

    /// <summary>
    ///     A hit on anything else that is not a mob: smoke only, as TGMC gives every object.
    /// </summary>
    public static readonly ProtoId<KsImpactMaterialPrototype> FallbackMaterial = "Default";

    /// <summary>
    ///     TGMC tells ballistic ammo from energy and xeno ammo by a flag; here a hit is ballistic if it pierces or
    ///         bludgeons.
    /// </summary>
    private static readonly ProtoId<DamageTypePrototype> PiercingDamage = "Piercing";
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    /// <summary>
    ///     TGMC's <c>sound_armor</c>, played when armour soaks a ballistic hit.
    /// </summary>
    private static readonly SoundSpecifier ArmorSound = new SoundCollectionSpecifier("KsBulletArmorImpact", AudioParams.Default.WithVariation(0.05f));

    /// <summary>
    ///     A ballistic hit on a mob that dealt less than this share of its damage counts as soaked by armour.
    /// </summary>
    public const float ArmorSoakFraction = 0.5f;

    /// <summary>
    ///     How far back along the shot <see cref="GetEntryPoint"/> starts looking for where it came in, in metres.
    /// </summary>
    private const float EntryCastBack = 1.5f;

    /// <summary>
    ///     How far off the surface an impact plays, so its emitter is not inside the wall.
    /// </summary>
    private const float EntrySurfaceOffset = 0.05f;

    // TGMC's playsound(source, sound_bounce, 50, TRUE, ...): half volume, varied.
    private static readonly AudioParams RicochetParams = AudioParams.Default.WithVolume(-6f).WithVariation(0.1f);

    /// <summary>
    ///     Plays a projectile's hit on <paramref name="target"/>, on this client only; does nothing on the server.
    /// </summary>
    /// <param name="coordinates">Where the projectile was when it hit: on the near side of the surface.</param>
    /// <param name="worldDirection">Which way the projectile was flying. The smoke and debris fly back against it.</param>
    /// <param name="rawDamage">The projectile's damage before anything resisted it.</param>
    /// <param name="dealtDamage">The damage the hit dealt, if any.</param>
    public void PlayImpact(EntityUid target, EntityCoordinates coordinates, Vector2 worldDirection, DamageSpecifier? rawDamage, DamageSpecifier? dealtDamage)
    {
        if (_netManager.IsServer || TerminatingOrDeleted(target))
            return;

        var ballistic = IsBallistic(rawDamage);

        if (_mobStateQuery.HasComp(target))
        {
            if (ballistic && IsSoaked(target, rawDamage!, dealtDamage))
                PlayHere(ArmorSound, coordinates, null);

            return;
        }

        if (!ProtoMan.TryIndex(PickMaterial(target), out var material))
            return;

        coordinates = GetEntryPoint(target, coordinates, worldDirection);
        var localRotation = GetLocalRotation(coordinates, -worldDirection);
        // Mostly a light hit, now and then the full burst.
        var dense = _random.Prob(material.DenseChance);
        var amount = dense ? 1f : material.LightAmount;
        var speed = dense ? 1f : material.LightSpeed;

        SpawnImpactParticles(material.Smoke, coordinates, localRotation, amount, speed);
        if (!ballistic)
            return;

        SpawnImpactParticles(material.Debris, coordinates, localRotation, amount, speed);
        if (material.Sound != null && _random.Prob(material.SoundChance))
            PlayHere(material.Sound, coordinates, RicochetParams);
    }

    /// <summary>
    ///     Plays a hit's impact from shared code both sides run, for a shot only its shooter's client predicts (a
    ///         hitscan): that client plays it at once; the server sends it to everyone else who can see it, as the
    ///         hitscan's beam sprites are.
    /// </summary>
    /// <param name="hitCoordinates">Where the shot met the target, on its near side.</param>
    /// <param name="user">Whoever fired it, whose client predicted it; none for a gun firing by itself.</param>
    public void PlayImpactPredicted(EntityUid target, MapCoordinates hitCoordinates, Vector2 worldDirection, DamageSpecifier? rawDamage, DamageSpecifier? dealtDamage, EntityUid? user)
    {
        var coordinates = _transformSystem.ToCoordinates(hitCoordinates);
        if (_netManager.IsClient)
        {
            if (_gameTiming.IsFirstTimePredicted)
                PlayImpact(target, coordinates, worldDirection, rawDamage, dealtDamage);

            return;
        }

        var filter = Filter.Pvs(hitCoordinates);
        if (user is { } userUid)
            filter.RemovePlayerByAttachedEntity(userUid);

        RaiseNetworkEvent(new KsImpactSurfaceHitEvent
        {
            Target = GetNetEntity(target),
            Coordinates = GetNetCoordinates(coordinates),
            Direction = worldDirection,
            RawDamage = rawDamage,
            Damage = dealtDamage,
        }, filter);
    }

    // Only a client plays these; one a client sent the server is ignored there (PlayImpact does nothing on a server).
    [SubscribeNetworkEvent]
    private void OnImpactSurfaceHit(KsImpactSurfaceHitEvent args)
    {
        if (TryGetEntity(args.Target, out var targetUid))
            PlayImpact(targetUid.Value, GetCoordinates(args.Coordinates), args.Direction, args.RawDamage, args.Damage);
    }

    /// <summary>
    ///     The material a hit on <paramref name="target"/> uses: its storage fill's with a chance of how full it is,
    ///         else its own, else <see cref="FallbackMaterial"/>.
    /// </summary>
    public ProtoId<KsImpactMaterialPrototype> PickMaterial(EntityUid target)
    {
        if (_storageFillQuery.TryComp(target, out var storageFillComponent) &&
            storageFillComponent.FillFraction > 0f &&
            _random.Prob(storageFillComponent.FillFraction))
            return storageFillComponent.Material;

        return _surfaceQuery.TryComp(target, out var surfaceComponent) ? surfaceComponent.Material : FallbackMaterial;
    }

    public static bool IsBallistic(DamageSpecifier? damage)
    {
        if (damage == null)
            return false;

        return damage.DamageDict.TryGetValue(PiercingDamage, out var piercing) && piercing > 0 ||
               damage.DamageDict.TryGetValue(BluntDamage, out var blunt) && blunt > 0;
    }

    /// <summary>
    ///     Whether armour (or other protection) soaked a hit on <paramref name="target"/>: it dealt less than
    ///         <see cref="ArmorSoakFraction"/> of what <paramref name="rawDamage"/> comes to after the target's own
    ///         resistances (its <see cref="DamageableComponent.DamageModifierSetId"/>, which is networked) and the global
    ///         damage modifiers. Those are taken out first, so a species that shrugs off bullets does not count as
    ///         armoured; what is left between the two is what <c>DamageModifyEvent</c> took, which is where armour works.
    /// </summary>
    public bool IsSoaked(EntityUid target, DamageSpecifier rawDamage, DamageSpecifier? dealtDamage)
    {
        var expectedDamage = rawDamage;
        if (_damageableQuery.TryComp(target, out var damageableComponent) &&
            damageableComponent.DamageModifierSetId is { } modifierSetId &&
            ProtoMan.Resolve(modifierSetId, out var modifierSet))
            expectedDamage = DamageSpecifier.ApplyModifierSet(expectedDamage, modifierSet);

        var expected = (float)_damageableSystem.ApplyUniversalAllModifiers(expectedDamage).GetTotal();
        if (expected <= 0f)
            return false;

        var dealt = dealtDamage == null ? 0f : (float)dealtDamage.GetTotal();
        return dealt < expected * ArmorSoakFraction;
    }

    /// <summary>
    ///     Where the shot came into <paramref name="target"/>: the projectile is often found inside or past a wall by
    ///         the time its contact is handled, and its impact would come out of the far side. Casts back along the
    ///         shot from a little behind it, against the target alone, and takes the nearest hit, stepped back off the
    ///         surface. Leaves <paramref name="coordinates"/> as they are when nothing is found.
    /// </summary>
    private EntityCoordinates GetEntryPoint(EntityUid target, EntityCoordinates coordinates, Vector2 worldDirection)
    {
        if (worldDirection.LengthSquared() <= float.Epsilon)
            return coordinates;

        var mapCoordinates = _transformSystem.ToMapCoordinates(coordinates);
        var direction = Vector2.Normalize(worldDirection);
        var start = mapCoordinates.Position - direction * EntryCastBack;
        var ray = new CollisionRay(start, direction, (int)CollisionGroup.AllMask);

        float? nearest = null;
        foreach (var hit in _physicsSystem.IntersectRayWithPredicate(mapCoordinates.MapId, ray, target,
                     static (uid, targetUid) => uid != targetUid, maxLength: EntryCastBack * 2f, returnOnFirstHit: false))
        {
            if (nearest == null || hit.Distance < nearest)
                nearest = hit.Distance;
        }

        // Nothing between the cast's start and the projectile: it stopped short of the surface, where it is.
        if (nearest is not { } distance || distance >= EntryCastBack)
            return coordinates;

        var entry = start + direction * Math.Max(0f, distance - EntrySurfaceOffset);
        return _transformSystem.ToCoordinates(coordinates.EntityId, new MapCoordinates(entry, mapCoordinates.MapId));
    }

    /// <summary>
    ///     Plays a sound for this client alone at <paramref name="coordinates"/>. Not <c>PlayLocal</c>, which is
    ///         <c>PlayPredicted</c> on the client and so silent on the re-predicted tick a kept projectile's hit can land
    ///         on; the callers make sure each hit plays once.
    /// </summary>
    private void PlayHere(SoundSpecifier sound, EntityCoordinates coordinates, AudioParams? audioParams)
    {
        _audioSystem.PlayStatic(sound, Filter.Local(), coordinates, recordReplay: false, audioParams ?? sound.Params);
    }

    /// <summary>
    ///     The rotation in the coordinates' parent's frame that points a prototype's -y along
    ///         <paramref name="worldDirection"/>. The emitter is square to that parent (followRotation off).
    /// </summary>
    private Angle GetLocalRotation(EntityCoordinates coordinates, Vector2 worldDirection)
    {
        if (worldDirection.LengthSquared() <= float.Epsilon)
            return Angle.Zero;

        var parentRotation = coordinates.EntityId.IsValid() ? _transformSystem.GetWorldRotation(coordinates.EntityId) : Angle.Zero;
        return Angle.FromWorldVec(Vector2.Normalize(worldDirection)) - parentRotation;
    }

    private void SpawnImpactParticles(ProtoId<KsParticlesPrototype>? prototype, EntityCoordinates coordinates, Angle localRotation, float amount, float speed)
    {
        if (prototype is not { } prototypeId ||
            _particlesSystem.SpawnLocal(prototypeId, coordinates, once: true) is not { } emitterUid)
            return;

        _particlesSystem.SetRotation(emitterUid, localRotation);
        _particlesSystem.SetAmount(emitterUid, amount);
        _particlesSystem.SetSpeed(emitterUid, speed);
    }

    #region Storage fill

    [SubscribeLocalEvent]
    private void OnStorageFillStartup(Entity<KsImpactSurfaceStorageFillComponent> entity, ref ComponentStartup args)
    {
        UpdateFillFraction(entity);
    }

    [SubscribeLocalEvent]
    private void OnStorageFillInserted(Entity<KsImpactSurfaceStorageFillComponent> entity, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            UpdateFillFraction(entity);
    }

    [SubscribeLocalEvent]
    private void OnStorageFillRemoved(Entity<KsImpactSurfaceStorageFillComponent> entity, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            UpdateFillFraction(entity);
    }

    private void UpdateFillFraction(Entity<KsImpactSurfaceStorageFillComponent> entity)
    {
        // The container does not exist before the storage starts up; empty until then.
        if (!TryComp<StorageComponent>(entity, out var storageComponent) ||
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            storageComponent.Container == null)
        {
            entity.Comp.FillFraction = 0f;
            return;
        }

        var capacity = storageComponent.Grid.GetArea();
        entity.Comp.FillFraction = capacity <= 0
            ? 0f
            : Math.Clamp((float)_storageSystem.GetCumulativeItemAreas((entity.Owner, storageComponent)) / capacity, 0f, 1f);
    }

    #endregion
}
