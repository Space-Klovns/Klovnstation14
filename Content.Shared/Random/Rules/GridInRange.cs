using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes; // KS14

namespace Content.Shared.Random.Rules;

/// <summary>
/// Returns true if on a grid or in range of one.
/// </summary>
public sealed partial class GridInRangeRule : RulesRule
{
    [DataField]
    public float Range = 10f;

    // KS14 Start
    /// <summary>
    ///     If notnull, the grid must have any of these components
    ///         to be considered
    /// </summary>
    [DataField]
    public ComponentRegistry? GridComponents = null;
    // KS14 End

    private List<Entity<MapGridComponent>> _grids = [];

    // KS14
    private bool CheckGridEligibleByComponents(EntityManager entManager, EntityUid gridUid)
    {
        foreach (var entry in GridComponents!)
        {
            if (!entManager.HasComponent(gridUid, entry.Value.Component.GetType()))
                continue;

            return true;
        }

        return false;
    }

    public override bool Check(EntityManager entManager, EntityUid uid)
    {
        if (!entManager.TryGetComponent(uid, out TransformComponent? xform))
        {
            return false;
        }

        if (xform.GridUid != null)
        {
            // KS14 Start
            if (GridComponents is { })
            {
                if (CheckGridEligibleByComponents(entManager, xform.GridUid.Value))
                    return !Inverted;
                else
                    return Inverted;
            }
            // KS14 End

            return !Inverted;
        }

        var transform = entManager.System<SharedTransformSystem>();
        var mapManager = IoCManager.Resolve<IMapManager>();

        var worldPos = transform.GetWorldPosition(xform);
        var gridRange = new Vector2(Range, Range);

        _grids.Clear();
        mapManager.FindGridsIntersecting(xform.MapID, new Box2(worldPos - gridRange, worldPos + gridRange), ref _grids);

        // KS14 Start
        foreach (var gridEntity in _grids)
        {
            if (GridComponents is { })
            {
                if (!CheckGridEligibleByComponents(entManager, gridEntity))
                    continue;

                return !Inverted;
            }
            else
                return !Inverted;
        }

        // No grids
        return Inverted;
        // KS14 End
    }
}
