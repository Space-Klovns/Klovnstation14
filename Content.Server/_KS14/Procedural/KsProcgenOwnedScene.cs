using System.Linq;
using System.Collections.Frozen;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._KS14.Procedural;

public sealed record KsProcgenOwnedStageBudgets
{
    public int MaximumTerrainCells { get; init; } = 4096;
    /// <summary>Shared native electrical scan/node/link work for one initialization transaction.</summary>
    public int MaximumElectricalWork { get; init; } = 65_536;
    public int MaximumSpawnedEntities { get; init; } = KsProcgenPreviewOperationSystem.MaximumAllocationsPerStage;
    /// <summary>Tile writes plus declared entity placements, excluding map/grid allocations.</summary>
    public int MaximumPlacementWork { get; init; } = 8192;
    public KsProcgenPrototypeManifestLimits PrototypeCapture { get; init; } = new();
    /// <summary>Four bounded image reconstructions per explicit entity, before allocation.</summary>
    public int MaximumPrototypeImageCharacters { get; init; } = 16_777_216;
}

internal sealed record KsProcgenOwnedSceneTile(Vector2i Cell, ushort TileId, string PrototypeId);
internal enum KsProcgenOwnedSceneEntityKind : byte { Wall, Window, Door, Furnishing, Light, Cable }
internal sealed record KsProcgenOwnedSceneEntity(string PrototypeId, Vector2 Position, int QuarterTurns,
    KsProcgenOwnedSceneEntityKind Kind)
{
    public string? ElectricalMemberId { get; init; }
    public KsProcgenElectricalVoltage? Voltage { get; init; }
}
internal sealed record KsProcgenOwnedSceneAssembly(string RegionId, KsProcgenAssemblyPosePlan Poses)
{
    public IReadOnlyList<KsProcgenRoomMemberBinding> Bindings { get; init; } = [];
}

/// <summary>Detached write intents. Only the request-to-stage transaction can construct or consume this scene.</summary>
internal sealed class KsProcgenOwnedScene
{
    internal KsProcgenOwnedScene(IEnumerable<KsProcgenOwnedSceneTile> tiles,
        IEnumerable<KsProcgenOwnedSceneEntity> entities, IEnumerable<KsProcgenOwnedSceneAssembly> assemblies,
        IEnumerable<Vector2i> floor, IEnumerable<Vector2i> blocked, KsProcgenOwnedElectricalLayout? electricalLayout = null,
        KsProcgenElectricalProfile? electricalProfile = null)
    {
        Tiles = Array.AsReadOnly(tiles.ToArray());
        Entities = Array.AsReadOnly(entities.ToArray());
        Assemblies = Array.AsReadOnly(assemblies.Select(assembly => assembly with
        {
            Bindings = Array.AsReadOnly(assembly.Bindings.ToArray()),
            Poses = new()
            {
                Status = assembly.Poses.Status, PoseHash = assembly.Poses.PoseHash,
                Members = Array.AsReadOnly(assembly.Poses.Members.Select(pose => pose with { Support = pose.Support with { } }).ToArray()),
            },
        }).ToArray());
        Floor = floor.ToFrozenSet();
        Blocked = blocked.ToFrozenSet();
        ElectricalLayout = electricalLayout;
        ElectricalProfile = electricalProfile;
    }

    internal IReadOnlyList<KsProcgenOwnedSceneTile> Tiles { get; }
    internal IReadOnlyList<KsProcgenOwnedSceneEntity> Entities { get; }
    internal IReadOnlyList<KsProcgenOwnedSceneAssembly> Assemblies { get; }
    internal FrozenSet<Vector2i> Floor { get; }
    internal FrozenSet<Vector2i> Blocked { get; }
    internal KsProcgenOwnedElectricalLayout? ElectricalLayout { get; }
    internal KsProcgenElectricalProfile? ElectricalProfile { get; }
    internal int EntityCount => Entities.Count + Assemblies.Sum(assembly => assembly.Poses.Members.Count);
    internal int PlacementWork => Tiles.Count + EntityCount;
}

/// <summary>One paused, disposable complete procedural scene; operation and publication remain separate gates.</summary>
public sealed class KsProcgenOwnedStage : KsProcgenStageHandle
{
    internal KsProcgenOwnedStage(MapId mapId, EntityUid mapUid, EntityUid gridUid,
        KsProcgenRequestSnapshot request, ulong proposalHash, KsProcgenOwnedScene scene, KsProcgenGasBindingResult? gas, KsProcgenElectricalPlan? electrical, int maximumElectricalWork,
        KsProcgenPrototypeManifest prototypes, List<EntityUid> allocations, int maximumEntities, IReadOnlyList<EntityUid> entities,
        IReadOnlyList<IReadOnlyDictionary<string, EntityUid>> assemblies)
        : base(mapId, mapUid, KsProcgenStageKind.Owned, allocations, maximumEntities)
    {
        GridUid = gridUid; Request = request; ProposalHash = proposalHash; Scene = scene;
        Entities = entities; Assemblies = assemblies;
        Prototypes = prototypes;
        Gas = gas; Electrical = electrical; MaximumElectricalWork = maximumElectricalWork;
    }

    public EntityUid GridUid { get; }
    public KsProcgenRequestSnapshot Request { get; }
    public ulong ProposalHash { get; }
    public KsProcgenPrototypeManifest Prototypes { get; }
    public KsProcgenGasBindingResult? Gas { get; }
    public KsProcgenElectricalPlan? Electrical { get; }
    public KsProcgenInitialElectricalReceipt? InitialElectrical { get; internal set; }
    internal int MaximumElectricalWork { get; }
    public KsProcgenInitialAtmosphereReceipt? InitialAtmosphere { get; internal set; }
    public IReadOnlyList<EntityUid> Entities { get; }
    /// <summary>Each dictionary is scoped to one room; member IDs may repeat in other rooms.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, EntityUid>> Assemblies { get; }
    internal KsProcgenOwnedScene Scene { get; }
    public bool EngineAccessVerified => false;
    public bool WorkingLightingVerified => false;
    public bool GasClosureVerified => false;
    public bool Published => false;
}

public enum KsProcgenOwnedStageStatus : byte
{
    PositionedPreview, InitializedPreview, InvalidInput, Rejected, BudgetExceeded,
    UnsupportedContent, Cancelled, EngineFailure,
}

public sealed record KsProcgenOwnedStageResult(KsProcgenOwnedStageStatus Status, KsProcgenIssue? Issue,
    KsProcgenOwnedStage? Stage, KsProcgenOwnedInteriorResult? Proposal);
