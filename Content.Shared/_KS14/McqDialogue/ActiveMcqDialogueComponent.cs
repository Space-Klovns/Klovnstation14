using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.McqDialogue;

[RegisterComponent, NetworkedComponent]
[UnsavedComponent]
public sealed partial class ActiveMcqDialogueComponent : Component
{
    public EntityUid Target = EntityUid.Invalid;
    public EntityUid User = EntityUid.Invalid;

    public List<string> OptionIds = [];
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
public record struct McqDialogueSelectedEvent(string Id);
