using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Chain;

/// <summary>
///     Denotes something as being part of a chain.
///         Basically a linked list.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class ChainLinkComponent : Component
{
    /// <summary>
    ///     The original (when the chain was/is fully intact)
    ///         edges of the chain. Edges may be missing from this
    ///         if they were destroyed/removed.
    /// </summary>
    [DataField]
    [Access(typeof(ChainSystem))]
    public List<EntityUid> EdgeUids = new();

    /// <summary>
    ///     Previous link in the chain. Will be null if that link was
    ///         destroyed or never existed.
    /// </summary>
    [DataField, AutoNetworkedField]
    [Access(typeof(ChainSystem), Other = AccessPermissions.Read)]
    public EntityUid? PreviousLinkUid = null;

    /// <summary>
    ///     Next link in the chain. Will be null if that link was
    ///         destroyed or never existed.
    /// </summary>
    [DataField, AutoNetworkedField]
    [Access(typeof(ChainSystem), Other = AccessPermissions.Read)]
    public EntityUid? NextLinkUid = null;

    /// <summary>
    ///     Joint to the previous link in the chain. Will be null if that link was
    ///         destroyed or never existed.
    /// </summary>
    [DataField, AutoNetworkedField]
    [Access(typeof(ChainSystem), Other = AccessPermissions.Read)]
    public string? PreviousLinkJointId = null;

    /// <summary>
    ///     Joint to the next link in the chain. Will be null if that link was
    ///         destroyed or never existed.
    /// </summary>
    [DataField, AutoNetworkedField]
    [Access(typeof(ChainSystem), Other = AccessPermissions.Read)]
    public string? NextLinkJointId = null;
}

/// <summary>
///     Denotes the original (when the chain was/is fully intact)
///         start and end of a chain.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class ChainEdgeComponent : Component
{
    /// <summary>
    ///     Is this chain broken?
    /// </summary>
    [DataField, AutoNetworkedField]
    [Access(typeof(ChainSystem), Other = AccessPermissions.Read)]
    public bool Broken = false;

    /// <summary>
    ///     Chain entities that have this listed as an edge.
    ///         Includes edges, as they are links too. So this list includes this edge too.
    /// </summary>
    [DataField]
    [Access(typeof(ChainSystem))]
    public List<EntityUid> LinkUids = new();

    /// <summary>
    ///     The other chain edge.
    ///         If it doesn't exist (anymore), this will be equal to
    ///         <see cref="EntityUid.Invalid"/>.
    /// </summary>
    [DataField]
    [Access(typeof(ChainSystem))]
    public EntityUid OtherEdgeUid = EntityUid.Invalid;

    /// <summary>
    ///     Joint to the other edge of the chain. Will be null if that link was
    ///         destroyed or never existed.
    /// </summary>
    [DataField]
    [Access(typeof(ChainSystem), Other = AccessPermissions.Read)]
    public string? StretchJointId = null;
}
