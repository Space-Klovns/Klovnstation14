using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._KS14.NPC.Hands;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Utility;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Inventory;
using Content.Shared.NPC;
using Content.Shared.Prying.Components;
using Content.Shared.Prying.Systems;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Doors;

/// <summary>
///     What NPCs believe about doors, and what they can force them with.
///     <list type="bullet">
///         <item><see cref="GetDoorAccess"/>: whether an NPC thinks it can open a door by hand. It goes by what the door
///             shows - its access list as it was built (what examining it shows), its bolts, a weld, whether it is
///             powered - not by what the door would actually do. So a door whose access was changed after it was built
///             can fool it.</item>
///         <item><see cref="TryGetBreachTool"/>: whether it carries something that forces the door - a prying tool,
///             or an access breaker - checked with the same rules the game uses.</item>
///         <item><see cref="ReportRefused"/>: a door that would not open for an NPC that believed it would. The door
///             becomes a no-go for it and for its squad, for a while, and it says so.</item>
///         <item><see cref="TryStartBreach"/>: forcing a door - tool out, used, and put back with the weapon back in
///             hand however it ends. For a <c>Breach</c> order, and for steering when the way it is going runs through
///             a door that will not open for it (<see cref="TryBreachBlockingDoor"/>).</item>
///     </list>
///     The queries are pure reads, safe from an HTN operator's <c>Plan</c> and from squad tactics.
/// </summary>
public sealed partial class NpcDoorSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private EmagSystem _emagSystem = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private PryingSystem _pryingSystem = default!;
    [Dependency] private SharedChargesSystem _sharedChargesSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedToolSystem _toolSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TagSystem _tagSystem = default!;
    [Dependency] private UseDelaySystem _useDelaySystem = default!;

    [Dependency] private EntityQuery<AirlockComponent> _airlockQuery = default!;
    [Dependency] private EntityQuery<DoorBoltComponent> _doorBoltQuery = default!;
    [Dependency] private EntityQuery<DoorComponent> _doorQuery = default!;
    [Dependency] private EntityQuery<EmagComponent> _emagQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<NpcBackgroundMoveComponent> _backgroundMoveQuery = default!;
    [Dependency] private EntityQuery<MultipleToolComponent> _multipleToolQuery = default!;
    [Dependency] private EntityQuery<NpcBreachingComponent> _breachingQuery = default!;
    [Dependency] private EntityQuery<NpcDoorUserComponent> _doorUserQuery = default!;
    [Dependency] private EntityQuery<NPCSteeringComponent> _steeringQuery = default!;
    [Dependency] private EntityQuery<PryingComponent> _pryingQuery = default!;

    private static readonly ProtoId<ToolQualityPrototype> PryingQuality = "Prying";

    /// <summary>
    ///     How long a breach steering starts (<see cref="TryBreachBlockingDoor"/>) is given before it is given up on.
    /// </summary>
    public static readonly TimeSpan DefaultBreachTimeout = TimeSpan.FromSeconds(10);

    private readonly List<EntityUid> _carriedItems = new();
    private readonly List<EntityUid> _endedBreaches = new();
}
