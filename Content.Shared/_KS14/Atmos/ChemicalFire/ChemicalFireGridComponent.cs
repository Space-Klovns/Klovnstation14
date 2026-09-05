using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Atmos.ChemicalFire;

/// <summary>
///     Cache of every chemfire on a grid, keyed by tile, so that spawning, deduplication and lateral
///         smoothing are all O(1) instead of needing an entity lookup per query.
///     Maintained entirely by <see cref="SharedChemicalFireSystem"/> off chemfire startup/shutdown.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedChemicalFireSystem), Other = AccessPermissions.ReadExecute)]
public sealed partial class ChemicalFireGridComponent : Component
{
    public readonly Dictionary<Vector2i, TileChemicalFireData<Entity<ChemicalFireComponent>>> Tiles = [];
}

/// <summary>
///     The chemfires occupying a single tile, keyed by
///         <see cref="ChemicalFireComponent.ConnectionKey"/>.
///     Generic because both sides hold <c>Entity&lt;ChemicalFireComponent&gt;</c> while the wire carries
///         <see cref="NetEntity"/>.
/// </summary>
/// <remarks>
///     Deliberately <see cref="SerializableAttribute"/> but not <c>NetSerializable</c>: NetSerializer scans
///         attributed types as roots and cannot take an open generic. The closed
///         <c>TileChemicalFireData&lt;NetEntity&gt;</c> is instead discovered transitively through
///         <see cref="ChemicalFireGridComponentState"/>, which is all the wire ever needs.
/// </remarks>
[Serializable]
public sealed class TileChemicalFireData<T>
{
    public Dictionary<string, T> Fires = [];
}

[Serializable, NetSerializable]
public sealed class ChemicalFireGridComponentState(Dictionary<Vector2i, TileChemicalFireData<NetEntity>> tiles) : ComponentState
{
    public Dictionary<Vector2i, TileChemicalFireData<NetEntity>> Tiles = tiles;
}
