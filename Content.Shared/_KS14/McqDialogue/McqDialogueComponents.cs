using Robust.Shared.Serialization;

namespace Content.Shared._KS14.McqDialogue;

[RegisterComponent, UnsavedComponent]
[Access(typeof(McqDialogueSystem), Other = AccessPermissions.Read)]
public sealed partial class ActiveMcqDialogueComponent : Component
{
    public Entity<McqDialogueSourceComponent> Source = default;
    public EntityUid User = EntityUid.Invalid;

    public List<string> OptionIds = [];
}

[RegisterComponent, UnsavedComponent]
[Access(typeof(McqDialogueSystem), Other = AccessPermissions.Read)]
public sealed partial class McqDialogueSourceComponent : Component
{
    public HashSet<Entity<ActiveMcqDialogueComponent>> Dialogues = new();
}

[Serializable, NetSerializable]
public sealed record class McqDialogueData(string Text, string Id);

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

[ByRefEvent]
public record struct McqDialogueClosedEvent;

[ByRefEvent]
public record struct McqDialogueSelectedEvent(string Id);
