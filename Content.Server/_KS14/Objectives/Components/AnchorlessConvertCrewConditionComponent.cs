using Content.Server._KS14.Objectives.Systems;

namespace Content.Server._KS14.Objectives.Components;

/// <summary>
/// Requires the Anchorless hive to comprise the configured fraction of the crew.
/// </summary>
[RegisterComponent, Access(typeof(AnchorlessObjectiveSystem))]
public sealed partial class AnchorlessConvertCrewConditionComponent : Component
{
    [DataField(required: true)]
    public float RequiredFraction;

    /// <summary>The number of crew members that must be converted to satisfy this objective.</summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int RequiredConversions;

    /// <summary>Minds remade by the conversion action, excluding the starting Anchorless.</summary>
    public HashSet<EntityUid> ConvertedMinds = new();
}
