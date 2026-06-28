using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Spawner;

[RegisterComponent]
public sealed partial class KsRandomCollectiveScopeComponent : Component
{
    [DataField]
    public Dictionary<EntProtoId, HashSet<EntityUid>> Spawners = [];
}
