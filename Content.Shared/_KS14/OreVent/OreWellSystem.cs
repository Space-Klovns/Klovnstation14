using Content.Shared.Explosion.EntitySystems;

namespace Content.Shared._KS14.OreVent;

/// <summary>
///     1984
/// </summary>
public sealed class OreWellSystem : EntitySystem
{
    [Dependency] private readonly SharedExplosionSystem _explosionSystem = default!;

    public void StartExtraction(EntityUid uid)
    {
        ClearAreaAround(uid);
    }

    private void ClearAreaAround(Entity<TransformComponent?> entity)
    {
        _explosionSystem.TriggerExplosive(entity.Owner, delete: false);
    }
}
