using Content.Server.Construction;
using Content.Shared._KS14.Klovnmed.OrganAttachmentConstruction;

namespace Content.Server._KS14.Klovnmed.OrganAttachmentConstruction;

public sealed class OrganAttachmentConstructionSystem : SharedOrganAttachmentConstructionSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OrganAttachmentConstructionComponent, ConstructionNodeChangedEvent>(OnNodeChanged);
    }

    private void OnNodeChanged(Entity<OrganAttachmentConstructionComponent> entity, ref ConstructionNodeChangedEvent args)
    {
        entity.Comp.NetNode = args.NewNode?.Name;
        DirtyField(entity, entity.Comp, nameof(entity.Comp.NetNode));
    }
}
