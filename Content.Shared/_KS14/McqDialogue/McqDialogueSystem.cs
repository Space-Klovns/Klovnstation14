using System.Numerics;

namespace Content.Shared._KS14.McqDialogue;

public sealed class McqDialogueSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _userInterfaceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveMcqDialogueComponent, BoundUIClosedEvent>(OnDialogueClosed);
        SubscribeLocalEvent<ActiveMcqDialogueComponent, McqDialogueDataSelectedMessage>(OnDataSelected);
    }

    public void CloseDialogue(Entity<ActiveMcqDialogueComponent?> dialogueEntity, Entity<McqDialogueSourceComponent>? sourceEntity = null)
    {
        if (!Resolve(dialogueEntity, ref dialogueEntity.Comp))
            return;

        sourceEntity ??= dialogueEntity.Comp.Source!;
        var sourceComponent = sourceEntity.Value.Comp;

        sourceComponent.Dialogues.Remove(dialogueEntity!);
        if (sourceComponent.Dialogues.Count == 0)
            RemComp(sourceEntity.Value, sourceComponent);

        PredictedQueueDel(dialogueEntity);
    }

    private void OnDialogueClosed(Entity<ActiveMcqDialogueComponent> entity, ref BoundUIClosedEvent args)
    {
        CloseDialogue(entity!, entity.Comp.Source);

        var ev = new McqDialogueClosedEvent();
        RaiseLocalEvent(entity.Comp.Source, ref ev);
    }

    private void OnDataSelected(Entity<ActiveMcqDialogueComponent> entity, ref McqDialogueDataSelectedMessage args)
    {
        if (!Exists(entity.Comp.Source))
            return;

        CloseDialogue(entity!, entity.Comp.Source);

        var ev = new McqDialogueSelectedEvent(args.Id);
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
        dialogueSourceComponent.Dialogues.Add((dialogueUid, dialogueComponent));
        dialogueComponent.Source = (sourceUid, dialogueSourceComponent);

        var uiComponent = Comp<UserInterfaceComponent>(dialogueUid);
        _userInterfaceSystem.SetUiState(
            (dialogueUid, uiComponent),
            McqDialogueUiKey.Key,
            new McqDialogueBoundUserInterfaceState([.. options])
        );
        _userInterfaceSystem.OpenUi((dialogueUid, uiComponent), McqDialogueUiKey.Key, userUid);

    }
}
