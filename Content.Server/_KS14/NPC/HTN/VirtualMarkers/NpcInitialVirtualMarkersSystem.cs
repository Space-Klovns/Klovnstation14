using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC.HTN;

namespace Content.Server._KS14.NPC.HTN.VirtualMarkers;

/// <summary>
///     Writes <see cref="NpcInitialVirtualMarkersComponent.Markers"/> into the HTN blackboard on map init.
/// </summary>
public sealed partial class NpcInitialVirtualMarkersSystem : EntitySystem
{
    [Dependency] private EntityQuery<HTNComponent> _htnQuery = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<NpcInitialVirtualMarkersComponent> entity, ref MapInitEvent args)
    {
        if (!_htnQuery.TryComp(entity, out var htnComponent))
            return;

        var blackboard = htnComponent.Blackboard;
        if (!blackboard.TryGetValue<HashSet<string>>(EnsureVirtualMarkerOperator.MarkerSet, out var markerSet, EntityManager))
        {
            markerSet = new HashSet<string>();
            blackboard.SetValue(EnsureVirtualMarkerOperator.MarkerSet, markerSet);
        }

        markerSet.UnionWith(entity.Comp.Markers);
    }
}
