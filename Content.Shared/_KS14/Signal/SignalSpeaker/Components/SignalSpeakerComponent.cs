using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared._KS14.Signal.SignalSpeaker.EntitySystems;

namespace Content.Shared._KS14.Signal.SignalSpeaker.Components;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedSignalSpeakerSystem))]
public sealed partial class SignalSpeakerComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), Access(Other = AccessPermissions.ReadWriteExecute)]
    [DataField]
    public string AssignedText = string.Empty;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public int MaxTextChars = 50;
}

[Serializable, NetSerializable]
public sealed class SignalSpeakerComponentState(string assignedText) : IComponentState
{
    public string AssignedText = assignedText;

    public int MaxTextChars;
}
