using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Trigger.Components;

/// <summary>
///     Leaves a chemical fire (<c>SharedChemicalFireSystem.SpawnChemicalFire</c>) on the tile it is on when triggered, and on
///         every floor tile within <see cref="Radius"/>: a flamer's round, landing.
/// </summary>
// Not networked: this type only exists in Content.Server (see SetHtnEnabledOnTriggerComponent).
[RegisterComponent]
public sealed partial class KsChemicalFireOnTriggerComponent : BaseXOnTriggerComponent
{
    [DataField(required: true)]
    public EntProtoId Fire;

    /// <summary>
    ///     How far round the tile it landed on to set alight, in tiles. Zero for that tile only.
    /// </summary>
    [DataField]
    public int Radius;

    /// <summary>
    ///     How long the fire burns, over the fire prototype's own duration. Null keeps that.
    /// </summary>
    [DataField]
    public TimeSpan? Duration;
}
