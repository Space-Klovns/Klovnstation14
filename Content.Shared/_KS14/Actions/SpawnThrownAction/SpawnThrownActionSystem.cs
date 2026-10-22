using Content.Shared.Throwing;
using Content.Shared._KS14.PredictedSpawning;

namespace Content.Shared._KS14.Actions.SpawnThrownAction;

/// <summary>
/// Handles action priming, confirmation and automatic unpriming.
/// </summary>
public sealed partial class SpawnThrownActionSystem : EntitySystem
{
    [Dependency] private KsSharedPredictedSpawnSystem _predictedSpawnSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;

    [SubscribeLocalEvent]
    private void OnSpawnThrownAction(SpawnThrownActionEvent args)
    {
        // TODO KS14: either dont predict this or fix whatever mispredict this may cause
        var uid = _predictedSpawnSystem.PredictedSpawnAttachedTo(args.Prototype, Transform(args.Performer).Coordinates, user: args.Performer);
        _throwingSystem.TryThrow(uid, args.Target, baseThrowSpeed: args.ThrowSpeed, user: args.Performer);
    }
}
