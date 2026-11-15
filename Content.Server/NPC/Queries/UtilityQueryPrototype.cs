using Content.Server.NPC.Queries.Considerations;
using Content.Server.NPC.Queries.Queries;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array; // KS14

namespace Content.Server.NPC.Queries;

/// <summary>
/// Stores data for generic queries.
/// Each query is run in turn to get the final available results.
/// These results are then run through the considerations.
/// </summary>
[Prototype]
public sealed partial class UtilityQueryPrototype : IPrototype, IInheritingPrototype // KS14: IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    // KS14 Start: a query can be another plus a consideration or two, and keep up with it
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<UtilityQueryPrototype>))]
    public string[]? Parents { get; private set; }

    [AbstractDataField]
    public bool Abstract { get; private set; }
    // KS14 End

    [ViewVariables(VVAccess.ReadWrite), DataField("query")]
    public List<UtilityQuery> Query = new();

    [ViewVariables(VVAccess.ReadWrite), DataField("considerations"), AlwaysPushInheritance] // KS14: AlwaysPushInheritance, a child adds its own to its parent's
    public List<UtilityConsideration> Considerations = new();

    /// <summary>
    /// How many entities we are allowed to consider. This is applied after all queries have run.
    /// </summary>
    [DataField("limit")]
    public int Limit = 128;
}
