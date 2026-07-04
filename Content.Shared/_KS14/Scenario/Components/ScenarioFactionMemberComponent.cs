using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Scenario.Components;

/// <summary>
///     This is used for marking a mob/thing as belonging to a certain faction for scenarios.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ScenarioFactionMemberComponent : Component
{
    /// <summary>
    ///     ID of the <see cref="ScenarioFactionPrototype"/> this
    ///         entity belongs to.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<ScenarioFactionPrototype> Id;
}
