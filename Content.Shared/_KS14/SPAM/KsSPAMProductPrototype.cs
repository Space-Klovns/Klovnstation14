using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.SPAM;

/// <summary>
///     Something a salvager can order from the S.P.A.M. with mining points, once their rank allows it.
/// </summary>
[Prototype]
public sealed partial class KsSPAMProductPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     What arrives in the pod. Its name and description are the product's.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Entity;

    /// <summary>
    ///     The price in mining points.
    /// </summary>
    [DataField(required: true)]
    public int Cost;

    /// <summary>
    ///     The tab the product is listed under.
    /// </summary>
    [DataField(required: true)]
    public LocId Category;

    /// <summary>
    ///     The lowest rank, counted from zero, that may order it.
    /// </summary>
    [DataField]
    public int MinRank;
}
