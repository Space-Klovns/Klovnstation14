using Robust.Shared.Serialization;

namespace Content.Shared._KS14.McqDialogue;

/// <summary>
///     Added to an entity that is an active (open) dialogue.
///         This holds the actual UI components.
/// </summary>
[RegisterComponent, UnsavedComponent]
[Access(typeof(McqDialogueSystem), Other = AccessPermissions.Read)]
public sealed partial class ActiveMcqDialogueComponent : Component
{
    public Entity<McqDialogueSourceComponent> Source = default;
    public EntityUid User = EntityUid.Invalid;

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

[Serializable, NetSerializable]
public sealed class McqDialogueBoundUserInterfaceState(List<McqDialogueData> dialogueData) : BoundUserInterfaceState
{
    public List<McqDialogueData> DialogueData = dialogueData;
}

[Serializable, NetSerializable]
public enum McqDialogueUiKey : byte { Key }

/// <summary>
///     Raised when the dialogue was closed, be it by user choice,
///         selecting something (which closes the dialogue), or something else.
///
///     Directed at the entity that is marked as the source of the dialogue.
/// </summary>
[ByRefEvent]
public record struct McqDialogueClosedEvent(EntityUid Actor);

/// <summary>
///     Raised when an option on the dialogue was selected.
///
///     Directed at the entity that is marked as the source of the dialogue.
/// </summary>
[ByRefEvent]
public record struct McqDialogueSelectedEvent(EntityUid Actor, string Id);
