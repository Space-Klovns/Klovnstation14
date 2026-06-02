using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Scenario.Components;

/// <summary>
/// This is used for tagging an entity as an objective for scenarios.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ScenarioObjectiveComponent : Component
{
    [DataField("isNt")] //literally what it says on the tin
    public bool isNt = false;
}
