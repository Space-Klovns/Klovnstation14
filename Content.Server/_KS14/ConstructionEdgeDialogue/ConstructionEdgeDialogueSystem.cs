using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Shared._KS14.McqDialogue;
using Content.Shared.Verbs;

namespace Content.Server._KS14.ConstructionEdgeDialogue;

public sealed class ConstructionEdgeDialogueSystem : EntitySystem
{
    [Dependency] private readonly ConstructionSystem _constructionSystem = default!;
    [Dependency] private readonly McqDialogueSystem _mcqDialogueSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ConstructionEdgeDialogueComponent, McqDialogueClosedEvent>(OnDialogueClosed);
        SubscribeLocalEvent<ConstructionEdgeDialogueComponent, McqDialogueSelectedEvent>(OnDialogueSelected);

        SubscribeLocalEvent<ConstructionEdgeDialogueComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerb);
    }

    private void OnDialogueClosed(Entity<ConstructionEdgeDialogueComponent> entity, ref McqDialogueClosedEvent args)
    {
        entity.Comp.CurrentNode = null;
    }

    private void OnDialogueSelected(Entity<ConstructionEdgeDialogueComponent> entity, ref McqDialogueSelectedEvent args)
    {
        if (!int.TryParse(args.Id, out var id))
            return;

        if (!TryComp<ConstructionComponent>(entity, out var constructionComponent) ||
            !entity.Comp.NodeNames.Contains(constructionComponent.Node) ||
            _constructionSystem.GetCurrentNode(entity.Owner, construction: constructionComponent) is not { } currentNode)
            return;

        if (currentNode.Name != entity.Comp.CurrentNode)
            return;

        var edge = currentNode.Edges[id];
        if (entity.Comp.BlacklistedTargets.Contains(edge.Target) ||
            !_constructionSystem.CheckConditions(entity.Owner, edge.Conditions))
            return;

        _constructionSystem.SetEdgeIndex(constructionComponent, id);
    }

    private void OnGetAltVerb(Entity<ConstructionEdgeDialogueComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess ||
            !args.CanComplexInteract ||
            !args.CanInteract)
            return;

        if (!TryComp<ConstructionComponent>(entity, out var constructionComponent) ||
            !entity.Comp.NodeNames.Contains(constructionComponent.Node) ||
            _constructionSystem.GetCurrentNode(entity.Owner, construction: constructionComponent) is not { } currentNode)
            return;

        var options = new List<McqDialogueData>();
        var index = 0;
        foreach (var edge in currentNode.Edges)
        {
            if (entity.Comp.BlacklistedTargets.Contains(edge.Target) ||
                !_constructionSystem.CheckConditions(entity.Owner, edge.Conditions))
            {
                index++;
                continue;
            }

            options.Add(new(edge.Target, index.ToString()));
            index++;
        }

        var userUid = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 2,
            Act = () => TryOpenDialogue(entity, userUid, options, currentNode.Name),
            Text = entity.Comp.Loc
        });
    }

    private void TryOpenDialogue(Entity<ConstructionEdgeDialogueComponent> entity, EntityUid userUid, List<McqDialogueData> options, string nodeName)
    {
        entity.Comp.CurrentNode = nodeName;
        _mcqDialogueSystem.StartDialogue(entity, userUid, options);
    }
}
