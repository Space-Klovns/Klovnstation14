using Content.Server.Construction.Components;

namespace Content.Server.Construction;

public sealed partial class ConstructionSystem
{
    public void SetEdgeIndex(ConstructionComponent component, int? value) => component.EdgeIndex = value;
}
