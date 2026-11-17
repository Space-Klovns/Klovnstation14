using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Actions.SpawnThrownAction;

public sealed partial class SpawnThrownActionEvent : WorldTargetActionEvent
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField]
    public float ThrowSpeed = 10f;
}
