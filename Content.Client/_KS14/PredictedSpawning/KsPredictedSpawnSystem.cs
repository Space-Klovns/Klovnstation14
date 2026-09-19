using Content.Shared._KS14.PredictedSpawning;
using Robust.Client.GameObjects;
using Robust.Client.Physics;

namespace Content.Client._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed partial class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    [SubscribeLocalEvent]
    private void OnPredictedSpawnCheckPhysicsPrediction(Entity<KsPredictedSpawnComponent> entity, ref UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }

    [SubscribeNetworkEvent]
    private void OnEntityNetworked(KsPredictedEntitySpawnedEvent args)
    {
        var uid = GetEntity(args.Entity);
        if (!uid.IsValid())
            return;

        PredictedDel(uid);
        RemComp<SpriteComponent>(uid);
    }
}
