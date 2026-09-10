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

    /// <summary>Anchorless minds recorded when this objective was assigned or a conversion completed.</summary>
    public HashSet<EntityUid> ConvertedMinds = new();
}
