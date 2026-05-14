using Content.Shared._KS14.PredictedSpawning;

namespace Content.Server._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    protected override EntityUid FlagPredictedAndReturn(EntityUid uid, EntityUid? user = null)
    {
        if (user is { })
        {
            var args = new KsPredictedEntitySpawnedEvent(GetNetEntity(uid));
            RaiseNetworkEvent(args, user.Value);
        }

        return base.FlagPredictedAndReturn(uid);
    }
}
