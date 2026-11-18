namespace Content.Shared.Cargo.Prototypes;

public sealed partial class CargoProductPrototype
{
    /// <summary>
    ///     Only offered by an emagged order console. tgstation's <c>ORDER_EMAG_ONLY</c>.
    /// </summary>
    [DataField]
    public bool KsEmagOnly { get; private set; }
}
