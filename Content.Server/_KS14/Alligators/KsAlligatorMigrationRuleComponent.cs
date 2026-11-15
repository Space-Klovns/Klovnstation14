using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Alligators;

/// <summary>
///     Configures how many alligators enter the station and which maintenance flooring can host them.
/// </summary>
[RegisterComponent, Access(typeof(KsAlligatorMigrationRule))]
public sealed partial class KsAlligatorMigrationRuleComponent : Component
{
    /// <summary>
    ///     Maximum number of alligators to spawn. Each one needs its own clear floor tile.
    /// </summary>
    [DataField]
    public int SpawnCount = 2;

    /// <summary>
    ///     Floor prototypes used to identify maintenance without searching unrelated station rooms.
    /// </summary>
    [DataField(required: true)]
    public HashSet<ProtoId<ContentTileDefinition>> MaintenanceTiles = new();
}
