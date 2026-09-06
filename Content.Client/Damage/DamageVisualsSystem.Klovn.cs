using Content.Shared.Damage.Components;
using Robust.Client.GameObjects;

namespace Content.Client.Damage;

public sealed partial class DamageVisualsSystem
{
    public void TryForceUpdateLayers(Entity<DamageableComponent?, SpriteComponent?, DamageVisualsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp1, logMissing: false) ||
            !Resolve(entity, ref entity.Comp2, logMissing: false) ||
            !Resolve(entity, ref entity.Comp3, logMissing: false))
            return;

        ForceUpdateLayers(entity!);
    }
}
