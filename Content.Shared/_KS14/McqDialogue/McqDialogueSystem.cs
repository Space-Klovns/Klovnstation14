using System.Numerics;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.McqDialogue;

public sealed partial class McqDialogueSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private SharedUserInterfaceSystem _userInterfaceSystem = default!;

    private static readonly EntProtoId<ActiveMcqDialogueComponent> DialogueProto = "McqDialogue";

    [SubscribeLocalEvent]
    private void OnDialogueSourceShutdown(Entity<McqDialogueSourceComponent> entity, ref ComponentShutdown args)
    {
        foreach (var dialogueEntity in entity.Comp.Dialogues)
            PredictedQueueDel(dialogueEntity);

        entity.Comp.Dialogues.Clear();
    }

    /// <summary>
    ///     Deletes the dialogue. Server only: the dialogue is the server's entity, which the client could only detach
    ///         until the next state put it back. The client closes its window with the UI's own predicted close.
    /// </summary>
    public void CloseDialogue(Entity<ActiveMcqDialogueComponent?> dialogueEntity)
    {
        if (_netManager.IsClient ||
            !Resolve(dialogueEntity, ref dialogueEntity.Comp) ||
            TerminatingOrDeleted(dialogueEntity.Comp.Source) ||
            TerminatingOrDeleted(dialogueEntity.Owner) ||
            dialogueEntity.Comp.Deleted)
            return;

        var sourceUid = dialogueEntity.Comp.Source;
        if (TryComp<McqDialogueSourceComponent>(sourceUid, out var sourceComponent))
        {
            sourceComponent.Dialogues.Remove(dialogueEntity!);
            if (sourceComponent.Dialogues.Count == 0)
                RemComp(sourceUid, sourceComponent);
        }

        // immediately remove it
        PredictedQueueDel(dialogueEntity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnDialogueClosed(Entity<ActiveMcqDialogueComponent> entity, ref BoundUIClosedEvent args)
    {
        CloseDialogue(entity!);

        var ev = new McqDialogueClosedEvent(args.Actor, entity.Owner);
        RaiseLocalEvent(entity.Comp.Source, ref ev);
    }

    [SubscribeLocalEvent]
    private void OnDataSelected(Entity<ActiveMcqDialogueComponent> entity, ref McqDialogueDataSelectedMessage args)
    {
        // The id comes from the client, so it's only trusted if it's one the dialogue offered. Also run on the
        //      answering client, predicted: the dialogue's data is networked so that the source can act on it there.
        if (!Exists(entity.Comp.Source) || !entity.Comp.OptionIds.Contains(args.Id))
            return;

        CloseDialogue(entity!);

        var ev = new McqDialogueSelectedEvent(args.Actor, args.Id, entity.Owner);
        RaiseLocalEvent(entity.Comp.Source, ref ev);
    }

    /// <summary>
    ///     Opens a dialogue about <paramref name="sourceUid"/> for <paramref name="userUid"/>, as a new entity on the
    ///         source. It stays open while the engine's UI checks pass for it - in range of the source and able to
    ///         interact - and while what <paramref name="settings"/> asks for holds. Requirement components on it get a
    ///         <see cref="McqDialogueStartedEvent"/> just before it opens.
    ///
    ///     Can be called from predicted code. Only the server makes the dialogue; the user's client opens its window
    ///         straight away (<see cref="McqDialoguePredictedEvent"/>), for the dialogue's UI to take over.
    /// </summary>
    /// <param name="settings">Title, prompt and requirements, usually a data field of the source's component.</param>
    /// <returns>The dialogue, or null on the client.</returns>
    public Entity<ActiveMcqDialogueComponent>? StartDialogue(
        EntityUid sourceUid,
        EntityUid userUid,
        IEnumerable<McqDialogueData> options,
        McqDialogueSettings? settings = null)
    {
        if (_netManager.IsClient)
        {
            if (_gameTiming.IsFirstTimePredicted && _playerManager.LocalEntity == userUid)
            {
                var predictedEvent = new McqDialoguePredictedEvent(sourceUid, [.. options], settings);
                RaiseLocalEvent(ref predictedEvent);
            }

            return null;
        }

        var dialogueUid = Spawn(DialogueProto, new(sourceUid, Vector2.Zero));
        var dialogueComponent = Comp<ActiveMcqDialogueComponent>(dialogueUid);
        dialogueComponent.User = userUid;
        foreach (var optionDatum in options)
            dialogueComponent.OptionIds.Add(optionDatum.Id);

        dialogueComponent.Source = sourceUid;
        Dirty(dialogueUid, dialogueComponent);
        EnsureComp<McqDialogueSourceComponent>(sourceUid).Dialogues.Add((dialogueUid, dialogueComponent));

        if (settings?.HeldItemWhitelist is { } heldItemWhitelist)
            EnsureComp<McqDialogueHeldItemComponent>(dialogueUid).Whitelist = heldItemWhitelist;

        var uiComponent = Comp<UserInterfaceComponent>(dialogueUid);
        if (settings?.Range is { } range &&
            _userInterfaceSystem.TryGetInterfaceData((dialogueUid, uiComponent), McqDialogueUiKey.Key, out var interfaceData))
            _userInterfaceSystem.SetUi((dialogueUid, uiComponent), McqDialogueUiKey.Key, new InterfaceData(interfaceData) { InteractionRange = range });

        var startedEvent = new McqDialogueStartedEvent(sourceUid, userUid);
        RaiseLocalEvent(dialogueUid, ref startedEvent);

        _userInterfaceSystem.OpenUi((dialogueUid, uiComponent), McqDialogueUiKey.Key, userUid);
        _userInterfaceSystem.SetUiState(
            (dialogueUid, uiComponent),
            McqDialogueUiKey.Key,
            new McqDialogueBoundUserInterfaceState(GetNetEntity(sourceUid), [.. options], settings?.Title, settings?.Prompt)
        );

        return (dialogueUid, dialogueComponent);
    }
}
