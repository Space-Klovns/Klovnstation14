using Content.Server.Construction;
using Content.Shared._KS14.McqDialogue;
using Content.Shared.Verbs;

namespace Content.Server._KS14.ConstructionPathfindingDialogue;

// TODO LCDC: optimise somehow

public sealed class ConstructionPathfindingDialogueSystem : EntitySystem
{
    [Dependency] private readonly ConstructionSystem _constructionSystem = default!;
    [Dependency] private readonly McqDialogueSystem _mcqDialogueSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ConstructionPathfindingDialogueComponent, McqDialogueSelectedEvent>(OnDialogueSelected);
        SubscribeLocalEvent<ConstructionPathfindingDialogueComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerb);
    }

    private void OnDialogueSelected(Entity<ConstructionPathfindingDialogueComponent> entity, ref McqDialogueSelectedEvent args)
    {
        _constructionSystem.SetPathfindingTarget(entity.Owner, args.Id);
    }

    private void OnGetAltVerb(Entity<ConstructionPathfindingDialogueComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess ||
            !args.CanComplexInteract ||
            !args.CanInteract)
            return;

        var userUid = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 2,
            Act = () => TryOpenDialogue(entity, userUid),
            Text = entity.Comp.Loc
        });
    }

    private void TryOpenDialogue(Entity<ConstructionPathfindingDialogueComponent> entity, EntityUid userUid)
    {
        var options = new List<McqDialogueData>();
        foreach (var item in entity.Comp.TargetDatums)
            options.Add(new(item, item));

        _mcqDialogueSystem.StartDialogue(entity, userUid, options);
    }
}
