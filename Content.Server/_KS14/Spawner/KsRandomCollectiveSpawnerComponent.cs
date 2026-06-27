using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Spawner;

[RegisterComponent]
[Access(typeof(KsRandomCollectiveSpawnerSystem))]
public sealed partial class KsRandomCollectiveSpawnerComponent : Component
{
    [DataField(required: true)]
    public EntProtoId ProtoId;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public KsRandomCollectiveSpawnScope Scope = KsRandomCollectiveSpawnScope.Grid;
}

public enum KsRandomCollectiveSpawnScope : byte
{
    Grid,
    Map,
    Global
}
