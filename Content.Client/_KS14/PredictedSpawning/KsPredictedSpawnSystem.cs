using Content.Shared._KS14.PredictedSpawning;

namespace Content.Client._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PredictedSpawnComponent, ComponentAdd>(OnPredictedSpawnEntityReconciled);
    }

    private void OnPredictedSpawnEntityReconciled(Entity<PredictedSpawnComponent> entity, ref ComponentAdd args)
    {
        QueueDel(entity);
    }

    protected override EntityUid FlagPredictedAndReturn(EntityUid uid)
        => uid;
}
