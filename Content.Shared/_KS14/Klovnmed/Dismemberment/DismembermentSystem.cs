using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Throwing;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Klovnmed.Dismemberment;

/// <summary>
///     All methods that search for bodyparts here assume that
///         all bodyparts will only either be contained in either a Body,
///         or another BodyPart.
/// </summary>
public sealed class DismembermentSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
    [Dependency] private readonly BodyPartSearchSystem _bodyPartSearchSystem = default!;

    private EntityQuery<BodyComponent> _bodyQuery;

    public override void Initialize()
    {
        base.Initialize();

        _bodyQuery = GetEntityQuery<BodyComponent>();
    }

    /// <summary>
    ///     Tries to dismember a random body-part of given type from someone,
    ///         setting its coordinates to those of the victim and throwing it in a random direction.
    ///         Does no throwing logic if <paramref name="throwSpeed"/> is exactly 0f.
    /// </summary>
    /// <returns>Whether anything happened.</returns>
    public bool TryDismemberRandomBodyPartOfType(Entity<BodyComponent?> bodyEntity, BodyPartType partType, [NotNullWhen(true)] out Entity<BodyPartComponent>? partEntity, Vector2? direction = null, float throwSpeed = 10f, EntityUid? user = null)
    {
        if (!_bodyQuery.Resolve(bodyEntity, ref bodyEntity.Comp, logMissing: false) ||
            !_bodyPartSearchSystem.TryGetRandomBodyPartOfType(bodyEntity, partType, out var predictedRandom, out partEntity))
        {
            partEntity = null;
            return false;
        }

        var partTransformComponent = Transform(partEntity.Value.Owner);
        _transformSystem.SetCoordinates(partEntity.Value.Owner, partTransformComponent, Transform(bodyEntity).Coordinates);

        if (throwSpeed != 0f)
        {
            direction ??= (predictedRandom ?? KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
                (int)_gameTiming.CurTick.Value,
                KsSharedRandomExtensions.GetNetId(partEntity.Value.Owner, EntityManager)
            )).NextUnitVector2();

            _throwingSystem.TryThrow(partEntity.Value.Owner, direction.Value, baseThrowSpeed: throwSpeed, user: user, recoil: false);
        }

        return true;
    }
}
