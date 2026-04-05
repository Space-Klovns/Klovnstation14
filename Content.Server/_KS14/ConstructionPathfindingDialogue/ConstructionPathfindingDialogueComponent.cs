using Robust.Shared.GameStates;

namespace Content.Server._KS14.ConstructionPathfindingDialogue;

[RegisterComponent, NetworkedComponent]
public sealed partial class ConstructionPathfindingDialogueComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string Loc = "Choose surgery target";

    [DataField(required: true)]
    public List<string> TargetDatums = [];
}
