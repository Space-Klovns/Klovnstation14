using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Movement.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class JetpackComponent : Component
{
    /// <summary>Whether the jetpack is turned on, but not whether it's actually flying!</summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = false;

    [DataField, AutoNetworkedField]
    public EntityUid? JetpackUser;

    [DataField]
    public float MoleUsage = 0.012f;

    [DataField]
    public EntProtoId ToggleAction = "ActionToggleJetpack";

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleActionEntity;

    [DataField]
    public float Acceleration = 1f;

    [DataField]
    public float Friction = 0.25f; // same as off-grid friction

    [DataField]
    public float WeightlessModifier = 1.2f;
}
