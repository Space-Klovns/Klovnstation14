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

    private void OnDialogueClosed(Entity<ActiveMcqDialogueComponent> entity, ref BoundUIClosedEvent args)
    {
        QueueDel(entity);
    }

    private void OnDataSelected(Entity<ActiveMcqDialogueComponent> entity, ref McqDialogueDataSelectedMessage args)
    {
        if (!Exists(entity.Comp.Target))
            return;

        QueueDel(entity);

        var ev = new McqDialogueSelected(args.Id);
        RaiseLocalEvent(entity.Comp.Target, ref ev);
    }

    public void StartDialogue(EntityUid sourceUid, EntityUid userUid, IEnumerable<McqDialogueData> options)
    {
        var uiUid = Spawn(null, new(sourceUid, Vector2.Zero));
        var uiComponent = EnsureComp<UserInterfaceComponent>(uiUid);

        var dialogueComponent = EnsureComp<ActiveMcqDialogueComponent>(uiUid);
        dialogueComponent.Target = sourceUid;
        dialogueComponent.User = userUid;
        dialogueComponent.Options.AddRange(options);

        _userInterfaceSystem.OpenUi((uiUid, uiComponent), McqDialogueUiKey.Key, userUid, predicted: false);
        _userInterfaceSystem.SetUiState((
            uiUid, uiComponent),
            McqDialogueUiKey.Key,
            new McqDialogueBoundUserInterfaceState(dialogueComponent.Options)
        );
    }
}
