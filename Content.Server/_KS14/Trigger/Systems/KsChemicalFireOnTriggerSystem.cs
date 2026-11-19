using Content.Server._KS14.Trigger.Components;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Trigger;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.Trigger.Systems;

public sealed partial class KsChemicalFireOnTriggerSystem : XOnTriggerSystem<KsChemicalFireOnTriggerComponent>
{
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    protected override void OnTrigger(Entity<KsChemicalFireOnTriggerComponent> entity, EntityUid targetUid, ref TriggerEvent args)
    {
        var coordinates = Transform(targetUid).Coordinates;
        if (_transformSystem.GetGrid(coordinates) is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return;

        var centre = _mapSystem.CoordinatesToTile(gridUid, gridComponent, coordinates);
        var radius = Math.Max(0, entity.Comp.Radius);
        var radiusSquared = radius * radius;

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (x * x + y * y > radiusSquared)
                    continue;

                _chemicalFireSystem.SpawnChemicalFire(entity.Comp.Fire, (gridUid, gridComponent), centre + new Vector2i(x, y), entity.Comp.Duration);
            }
        }

        args.Handled = true;
    }
}
