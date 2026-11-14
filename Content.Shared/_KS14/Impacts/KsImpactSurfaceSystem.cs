using System.Numerics;
using Content.Shared._KS14.Particles;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Components;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._KS14.Impacts;

/// <summary>
///     TGMC's bullet impacts (<c>/datum/element/debris</c>): a hit on a surface puffs smoke back out of it, a ballistic
///         one throws off debris of the surface's <see cref="KsImpactMaterialPrototype"/> and maybe ricochets, and a
///         ballistic hit on a mob that its armour mostly soaked plays an armour sound.
///
///     All of it is cosmetic and fired for every bullet, so it only ever plays on a client, for itself
///         (<see cref="PlayImpact"/>): from the hit it predicted, or from the server's relayed hit when it predicted
///         none. Nothing is networked per shot.
/// </summary>
public sealed partial class KsImpactSurfaceSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedStorageSystem _storageSystem = default!;
    [Dependency] private KsSharedParticlesSystem _particlesSystem = default!;

    [Dependency] private EntityQuery<KsImpactSurfaceComponent> _surfaceQuery = default!;
    [Dependency] private EntityQuery<KsImpactSurfaceStorageFillComponent> _storageFillQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

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
            if (ballistic && IsSoaked(rawDamage!, dealtDamage))
                _audioSystem.PlayLocal(ArmorSound, target, null);

            return;
        }

        if (!ProtoMan.TryIndex(PickMaterial(target), out var material))
            return;

        var localRotation = GetLocalRotation(coordinates, -worldDirection);
        SpawnImpactParticles(material.Smoke, coordinates, localRotation);
        if (!ballistic)
            return;

        SpawnImpactParticles(material.Debris, coordinates, localRotation);
        if (material.Sound != null && _random.Prob(material.SoundChance))
            _audioSystem.PlayLocal(material.Sound, target, null, RicochetParams);
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

    public static bool IsSoaked(DamageSpecifier rawDamage, DamageSpecifier? dealtDamage)
    {
        var raw = (float)rawDamage.GetTotal();
        if (raw <= 0f)
            return false;

        var dealt = dealtDamage == null ? 0f : (float)dealtDamage.GetTotal();
        return dealt < raw * ArmorSoakFraction;
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

    private void SpawnImpactParticles(ProtoId<KsParticlesPrototype>? prototype, EntityCoordinates coordinates, Angle localRotation)
    {
        if (prototype is not { } prototypeId ||
            _particlesSystem.SpawnLocal(prototypeId, coordinates) is not { } emitterUid)
            return;

        _particlesSystem.SetRotation(emitterUid, localRotation);
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
