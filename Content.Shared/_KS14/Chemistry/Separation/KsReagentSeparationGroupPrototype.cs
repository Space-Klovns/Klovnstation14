using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Chemistry.Separation;

/// <summary>
///     A group of reagents that machines cannot pick apart. Reagents join it with
///         <see cref="Content.Shared.Chemistry.Reagent.ReagentPrototype.KsInseparableGroup"/>.
/// </summary>
[Prototype]
public sealed partial class KsReagentSeparationGroupPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     What players call the group, for the popup when a machine moves all of it.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;
}
