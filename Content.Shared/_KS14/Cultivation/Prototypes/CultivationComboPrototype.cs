using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation.Prototypes;

/// <summary>
///     When a cultivator's two laws are of these two elements, their qi resonates and opens a technique of its own.
/// </summary>
[Prototype]
public sealed partial class CultivationComboPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public CultivationElement ElementOne;

    [DataField(required: true)]
    public CultivationElement ElementTwo;

    [DataField]
    public CultivationRealm Realm = CultivationRealm.FoundationEstablishment;

    [DataField(required: true)]
    public EntProtoId Action;
}
