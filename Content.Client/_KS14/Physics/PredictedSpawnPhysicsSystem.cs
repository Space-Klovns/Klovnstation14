
using Robust.Client.Physics;

namespace Content.Client._KS14.Physics;

/// <summary>
///     Handles predicting physics update for entities with
///         <see cref="PredictedSpawnComponent"/>. 
/// </summary>
public sealed class PredictedSpawnPhysicsSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PredictedSpawnComponent, UpdateIsPredictedEvent>(OnPredictedSpawnPhysicsUpdatePredictionAttempt);
    }

    private void OnPredictedSpawnPhysicsUpdatePredictionAttempt(Entity<PredictedSpawnComponent> entity, ref UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }
}
