using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Meters;

/// <summary>
///     Raises a meter on this NPC whenever one of its squadmates goes down: critical, then dead. With caution as the
///         meter, losing people makes a squad careful. See <see cref="NpcMeterOnSquadLossSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcMeterOnSquadLossComponent : Component
{
    [DataField(required: true)]
    public ProtoId<NpcMeterPrototype> Meter;

    /// <summary>
    ///     Added when a squadmate goes critical.
    /// </summary>
    [DataField]
    public float Critical = 35f;

    /// <summary>
    ///     Added when a squadmate dies, on top of anything added when it went critical.
    /// </summary>
    [DataField]
    public float Dead = 50f;
}
