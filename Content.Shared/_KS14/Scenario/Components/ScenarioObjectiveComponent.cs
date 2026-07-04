using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Scenario.Components;

/// <summary>
///     This is used for tagging an entity as an objective for scenarios.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ScenarioObjectiveComponent : Component
{
    // ffs
    /// <summary>
    ///     Trigger keys to trigger a win.
    /// </summary>
    [DataField]
    public HashSet<string> KeysIn = [];

    /// <summary>
    ///     ID of the <see cref="ScenarioFactionPrototype"/> this
    ///         entity belongs to.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<ScenarioFactionPrototype> FactionId;
}
