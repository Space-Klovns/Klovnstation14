using Content.Shared._KS14.PredictedSpawning;

namespace Content.Server._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed partial class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    protected override void KeepForUser(EntityUid uid, EntityUid user)
    {
        SetPredictingUser((uid, EnsureComp<KsPredictedSpawnComponent>(uid)), user);
    }
}
