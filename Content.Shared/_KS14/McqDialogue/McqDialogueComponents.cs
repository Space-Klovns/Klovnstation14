using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.McqDialogue;

/// <summary>
///     Added to an entity that is an active (open) dialogue.
///         This holds the actual UI components. Networked so that the answering client can predict what the answer
///         does.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
[Access(typeof(McqDialogueSystem), Other = AccessPermissions.Read)]
public sealed partial class ActiveMcqDialogueComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid Source;

    [ViewVariables, AutoNetworkedField]
    public EntityUid User = EntityUid.Invalid;

    [ViewVariables, AutoNetworkedField]
    public List<string> OptionIds = [];
}

/// <summary>
///     Added to an entity that is the source of an active (open)
///         dialogue.
/// </summary>
[RegisterComponent, UnsavedComponent]
[Access(typeof(McqDialogueSystem), Other = AccessPermissions.Read)]
public sealed partial class McqDialogueSourceComponent : Component
{
    public HashSet<Entity<ActiveMcqDialogueComponent>> Dialogues = new();
}

/// <param name="LocId">Locale ID of the text to be used.</param>
[Serializable, NetSerializable]
public sealed record class McqDialogueData(LocId LocId, string Id);

[Serializable, NetSerializable]
public sealed class McqDialogueDataSelectedMessage(string id) : BoundUserInterfaceMessage
{
    public string Id = id;
}

/// <param name="source">The entity the dialogue is about, which picks out the window the client already opened for it.</param>
/// <param name="title">Locale ID of the window's title, or null for the default.</param>
/// <param name="prompt">Locale ID of the question above the options, or null for the default.</param>
[Serializable, NetSerializable]
public sealed partial class McqDialogueBoundUserInterfaceState(NetEntity source, List<McqDialogueData> dialogueData, LocId? title = null, LocId? prompt = null) : BoundUserInterfaceState
{
    public NetEntity Source = source;
    public List<McqDialogueData> DialogueData = dialogueData;
    public LocId? Title = title;
    public LocId? Prompt = prompt;
}

[Serializable, NetSerializable]
public enum McqDialogueUiKey : byte { Key }

/// <summary>
///     Raised when the dialogue was closed, be it by user choice,
///         selecting something (which closes the dialogue), or something else.
///
///     Directed at the entity that is marked as the source of the dialogue.
/// </summary>
/// <param name="Dialogue">The dialogue entity, still there until the end of the tick.</param>
[ByRefEvent]
public record struct McqDialogueClosedEvent(EntityUid Actor, EntityUid Dialogue);

/// <summary>
///     Raised when an option on the dialogue was selected.
///
///     Directed at the entity that is marked as the source of the dialogue.
/// </summary>
/// <param name="Id">One of the ids the dialogue was started with.</param>
/// <param name="Dialogue">
///     The dialogue entity, still there until the end of the tick, so the source can read what its components recorded
///         (<see cref="McqDialogueHeldItemComponent.HeldUid"/>).
/// </param>
[ByRefEvent]
public record struct McqDialogueSelectedEvent(EntityUid Actor, string Id, EntityUid Dialogue);

/// <summary>
///     Raised on a new dialogue entity once it is set up, just before its UI opens: its source and user are set, so
///         components on it can take what they need from them.
/// </summary>
[ByRefEvent]
public readonly record struct McqDialogueStartedEvent(EntityUid Source, EntityUid User);

/// <summary>
///     Raised, broadcast, on the user's client when shared code starts a dialogue for them. Only the server makes the
///         dialogue itself, so the client opens its window now and the dialogue's UI takes it over once it arrives.
/// </summary>
[ByRefEvent]
public readonly record struct McqDialoguePredictedEvent(EntityUid Source, List<McqDialogueData> Options, McqDialogueSettings? Settings);
