using System.Numerics;

namespace Content.Shared._KS14.McqDialogue;

public sealed class McqDialogueSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _userInterfaceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<McqDialogueSourceComponent, ComponentShutdown>(OnDialogueSourceShutdown);

        SubscribeLocalEvent<ActiveMcqDialogueComponent, BoundUIClosedEvent>(OnDialogueClosed);
        SubscribeLocalEvent<ActiveMcqDialogueComponent, McqDialogueDataSelectedMessage>(OnDataSelected);
    }


    private void OnDialogueSourceShutdown(Entity<McqDialogueSourceComponent> entity, ref ComponentShutdown args)
    {
        foreach (var dialogueEntity in entity.Comp.Dialogues)
            PredictedQueueDel(dialogueEntity);

        entity.Comp.Dialogues.Clear();
    }

    public void CloseDialogue(Entity<ActiveMcqDialogueComponent?> dialogueEntity)
    {
        if (!Resolve(dialogueEntity, ref dialogueEntity.Comp) ||
            TerminatingOrDeleted(dialogueEntity.Comp.Source) ||
            TerminatingOrDeleted(dialogueEntity.Owner) ||
            dialogueEntity.Comp.Deleted)
            return;

        var sourceEntity = dialogueEntity.Comp.Source!;
        var sourceComponent = sourceEntity.Comp;

        sourceComponent.Dialogues.Remove(dialogueEntity!);
        if (sourceComponent.Dialogues.Count == 0)
            RemComp(sourceEntity, sourceComponent);

        // immediately remove it
        PredictedQueueDel(dialogueEntity.Owner);
    }

    private void OnDialogueClosed(Entity<ActiveMcqDialogueComponent> entity, ref BoundUIClosedEvent args)
    {
        CloseDialogue(entity!);

        var ev = new McqDialogueClosedEvent(args.Actor);
        RaiseLocalEvent(entity.Comp.Source, ref ev);
    }

    private void OnDataSelected(Entity<ActiveMcqDialogueComponent> entity, ref McqDialogueDataSelectedMessage args)
    {
        if (!Exists(entity.Comp.Source))
            return;

        CloseDialogue(entity!);

        var ev = new McqDialogueSelectedEvent(args.Actor, args.Id);
        RaiseLocalEvent(entity.Comp.Source, ref ev);
    }

    public void StartDialogue(EntityUid sourceUid, EntityUid userUid, IEnumerable<McqDialogueData> options)
    {
        var dialogueUid = Spawn("McqDialogue", new(sourceUid, Vector2.Zero));
        var dialogueComponent = Comp<ActiveMcqDialogueComponent>(dialogueUid);
        dialogueComponent.User = userUid;
        foreach (var optionDatum in options)
            dialogueComponent.OptionIds.Add(optionDatum.Id);

        var dialogueSourceComponent = EnsureComp<McqDialogueSourceComponent>(sourceUid);
        dialogueComponent.Source = (sourceUid, dialogueSourceComponent);
        dialogueSourceComponent.Dialogues.Add((dialogueUid, dialogueComponent));

        var uiComponent = Comp<UserInterfaceComponent>(dialogueUid);

        _userInterfaceSystem.OpenUi((dialogueUid, uiComponent), McqDialogueUiKey.Key, userUid);
        _userInterfaceSystem.SetUiState(
            (dialogueUid, uiComponent),
            McqDialogueUiKey.Key,
            new McqDialogueBoundUserInterfaceState([.. options])
        );
    }
}
