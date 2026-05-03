using Content.Server.Construction.Components;
using Content.Shared.Construction;

namespace Content.Server.Construction;

public sealed partial class ConstructionSystem
{
    private void InitializeKlovn()
    {
        SubscribeLocalEvent<ConstructionComponent, ComponentShutdown>(OnConstructionShutdown);
    }

    private void OnConstructionShutdown(Entity<ConstructionComponent> entity, ref ComponentShutdown args)
    {
        RaiseNodeChangeEvent(entity, null);
    }

    public static void SetEdgeIndex(ConstructionComponent component, int? value) => component.EdgeIndex = value;

    private void RaiseNodeChangeEvent(EntityUid uid, ConstructionGraphNode? node)
    {
        var ev = new ConstructionNodeChangedEvent(node);
        RaiseLocalEvent(uid, ref ev);
    }
}

/// <summary>
///     KS14: Raised on something when its current construction node
///         is changed.
/// </summary>
/// <param name="NewNode">The new node. May be null for various reasons.</param>
[ByRefEvent]
public record struct ConstructionNodeChangedEvent(ConstructionGraphNode? NewNode);
