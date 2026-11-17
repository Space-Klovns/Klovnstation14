namespace Content.Server._KS14.NPC.HTN.VirtualMarkers;

/// <summary>
///     Virtual markers (see <see cref="PrimitiveTasks.Operators.EnsureVirtualMarkerOperator"/>) written into the NPC's
///         HTN blackboard on map init, so a variant of an NPC can start partway through its behaviour.
/// </summary>
[RegisterComponent, Access(typeof(NpcInitialVirtualMarkersSystem))]
public sealed partial class NpcInitialVirtualMarkersComponent : Component
{
    [DataField(required: true)]
    public List<string> Markers = new();
}
