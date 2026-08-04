using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Anchorless.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class AnchorlessCommunionComponent : Component
{
    [DataField]
    public EntProtoId? CommunionAction = "ActionAnchorlessCommunion";
}

public sealed partial class AnchorlessCommunionActionEvent : EntityTargetActionEvent;
