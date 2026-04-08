using System.Numerics;
using Content.Shared._KS14.Klovnmed.Dismemberment;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body;
using Content.Shared.Damage.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Klovnmed.DismembermentByDamage;

public sealed class DismembermentByDamageSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly DismembermentSystem _dismembermentSystem = default!;

    /// <summary>
    ///     How much of the damage accumulated is lost every second.
    /// </summary>
    public const float DecayCoefficient = 0.65f;

    /// <summary>
    ///     How much of the damage accumulated is lost every second.
    /// </summary>
    public static readonly TimeSpan FuckupCooldown = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DismembermentByDamageComponent, ComponentStartup>(OnInit);
        SubscribeLocalEvent<DismembermentByDamageComponent, DamageChangedEvent>(OnDamageTaken);
    }

    private void OnInit(Entity<DismembermentByDamageComponent> entity, ref ComponentStartup args)
    {
        if (entity.Comp.LastUpdate != TimeSpan.MinValue)
            return;

        entity.Comp.LastUpdate = _gameTiming.CurTime;
        Dirty(entity);
    }

    private void OnDamageTaken(Entity<DismembermentByDamageComponent> entity, ref DamageChangedEvent args)
    {
        // Thank you masulita
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        if (!args.DamageIncreased ||
            args.DamageDelta is not { } totalDamageDelta ||
            !totalDamageDelta.DamageDict.TryGetValue(entity.Comp.DamageProtoId, out var damageDeltaFp2))
            return;

        var damageDelta = (float)damageDeltaFp2;
        if (damageDelta <= entity.Comp.DeltaDamageThreshold)
            return;

        entity.Comp.LastAccumulatedDamage = LazyEvaluateAccumulatedDamageSince(entity.Comp) + damageDelta;
        entity.Comp.LastUpdate = _gameTiming.CurTime;

        if (_gameTiming.CurTime > entity.Comp.NextFuckup)
            TryFuckUp(entity, args.Origin);

        Dirty(entity);
    }

    private void TryFuckUp(Entity<DismembermentByDamageComponent> entity, EntityUid? origin = null)
    {
        if (entity.Comp.LastAccumulatedDamage < entity.Comp.AccumulatedDamageThreshold ||
            !TryComp<BodyComponent>(entity, out var bodyComponent))
            return;

        var eligibleUids = new ValueList<EntityUid>();
        foreach (var (category, organEntity) in bodyComponent.PresentOrganCategories)
        {
            if (!entity.Comp.OrganCategories.Contains(category))
                continue;

            eligibleUids.Add(organEntity.Owner);
        }

        if (eligibleUids.Count == 0)
            return;

        var deltaUnit = Vector2.Zero;
        if (origin is { })
        {
            deltaUnit = _transformSystem.GetWorldPosition(entity.Owner) - _transformSystem.GetWorldPosition(origin.Value);
            Vector2Helpers.Normalize(ref deltaUnit);
        }

        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            (int)_gameTiming.CurTick.Value,
            KsSharedRandomExtensions.GetNetId(entity.Owner, EntityManager),
            eligibleUids.Count
        );
        _dismembermentSystem.DismemberPart(entity.Owner, predictedRandom.Pick(eligibleUids), direction: deltaUnit + predictedRandom.NextVector2(1.5f), throwSpeed: 12f, cause: origin, predictedRandom: predictedRandom);

        entity.Comp.NextFuckup = _gameTiming.CurTime + FuckupCooldown;
    }

    public static float LazyEvaluateAccumulatedDamage(float accumulatedDamage, float deltaTime)
        => accumulatedDamage * MathF.Pow(DecayCoefficient, deltaTime);

    public float LazyEvaluateAccumulatedDamageSince(DismembermentByDamageComponent component)
        => LazyEvaluateAccumulatedDamage(component.LastAccumulatedDamage, (float)(_gameTiming.CurTime.TotalSeconds - component.LastUpdate.TotalSeconds));
}
