namespace Content.Server._KS14.ConstructionEdgeDialogue;

[RegisterComponent]
public sealed partial class ConstructionEdgeDialogueComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public LocId Loc = "Choose surgery target";

    [DataField]
    public HashSet<string> NodeNames = [];

    [DataField]
    public HashSet<string> BlacklistedTargets = [];

    [DataField]
    public string? CurrentNode = null;
}
