using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Spawner;

/// <summary>
///     When added to a grid/map/whatever that is a valid scope (see <see cref="Scope"/>),
///         upon mapinit of that scope, one randomly picked spawner for each entity prototype added
///         will spawn the given entity. After this process is completed, all spawners of that scope are deleted.
///
///     Scope is not updated when the spawner moves.
/// </summary>
[RegisterComponent]
[Access(typeof(KsRandomCollectiveSpawnerSystem))]
public sealed partial class KsRandomCollectiveSpawnerComponent : Component
{
    [DataField(required: true)]
    public EntProtoId ProtoId;

    /// <summary>
    ///     Whether this only works on grids or on maps.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public KsRandomCollectiveSpawnScope Scope = KsRandomCollectiveSpawnScope.Grid;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? AttachedScopeUid = null;
}

public enum KsRandomCollectiveSpawnScope : byte
{
    Grid,
    Map
}
