using Content.Shared._KS14.PredictedSpawning;

namespace Content.Server._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    protected override EntityUid FlagPredictedAndReturn(EntityUid uid)
    {
        AddComp<KsPredictedSpawnComponent>(uid);
        return uid;
    }
}
