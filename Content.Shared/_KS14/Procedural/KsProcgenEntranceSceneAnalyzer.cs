using System.Globalization;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Declared clean walking cells and permanently blocked threshold cells, not a live collision query.</summary>
public sealed record KsProcgenEntranceScene(IReadOnlySet<Vector2i> CleanDomainCells,
    IReadOnlySet<Vector2i> CleanHostCells, IReadOnlySet<Vector2i> BlockedThresholdCells, bool HostContextComplete);

public sealed record KsProcgenEntranceComponentWitness(int Component, IReadOnlyList<Vector2i> Cells, IReadOnlyList<string> GroupIds);
public sealed record KsProcgenEntrancePortWitness(string PortId, int LocalComponent, int ScopeComponent, int? GlobalComponent);

public enum KsProcgenEntranceSceneStatus : byte
{
    Candidate,
    InvalidInput,
    Rejected,
    BudgetExceeded,
}

public sealed class KsProcgenEntranceSceneResult
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public string? ConfigurationId { get; init; }
    public IReadOnlyList<KsProcgenEntranceComponentWitness> Components { get; init; } = [];
    public IReadOnlyList<KsProcgenEntrancePortWitness> Ports { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<Vector2i>> HostComponents { get; init; } = [];
    public IReadOnlyList<string> SealedPorts { get; init; } = [];
    public int ExpandedCells { get; init; }
    public ulong SceneHash { get; init; }
    public bool EngineAccessVerified => false;
}

/// <summary>Atomic component checks for one complete configuration and one complete proposed scene.</summary>
public static class KsProcgenEntranceSceneAnalyzer
{
    private static readonly Vector2i[] Steps = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static KsProcgenEntranceSceneResult Analyze(KsProcgenRequest request, string configurationId,
        KsProcgenEntranceScene scene, int maximumExpandedCells = 131_072)
    {
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenEntranceSceneStatus.InvalidInput, Issue = issue };
        return AnalyzeNormalized(shape!, request.ConnectivityPolicy, request.RootCells, configurationId, scene, maximumExpandedCells);
    }

    internal static KsProcgenEntranceSceneResult AnalyzeNormalized(KsProcgenNormalizedShape shape,
        KsProcgenConnectivityPolicy connectivityPolicy, IReadOnlyList<Vector2i> roots, string configurationId,
        KsProcgenEntranceScene scene, int maximumExpandedCells)
    {
        var expanded = 0;
        KsProcgenEntranceSceneResult Fail(KsProcgenEntranceSceneStatus status, string code) => new()
        {
            Status = status, Issue = new(code, $"Entrance scene for configuration '{configurationId}' did not validate."), ExpandedCells = expanded,
        };
        var configuration = shape.EntranceContract?.Configurations.SingleOrDefault(configuration => configuration.Id == configurationId);
        if (configuration == null || configuration.Weight <= 0f || scene == null || scene.CleanDomainCells == null ||
            scene.CleanHostCells == null || scene.BlockedThresholdCells == null || maximumExpandedCells is < 0 or > 131_072 ||
            scene.CleanDomainCells.Count + (long) scene.CleanHostCells.Count > 131_072 || scene.BlockedThresholdCells.Count > 65_536 ||
            roots.Count > 65_536 || scene.CleanDomainCells.Any(cell => !shape.ContainsTarget(cell)) ||
            scene.CleanHostCells.Any(cell => shape.ContainsTarget(cell) || !ValidCell(cell)) ||
            scene.BlockedThresholdCells.Any(cell => !ValidCell(cell) || scene.CleanDomainCells.Contains(cell) || scene.CleanHostCells.Contains(cell)) ||
            roots.Any(cell => !shape.ContainsTarget(cell)))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidEntranceScene");
        if (configuration.IsolationScope == KsProcgenEntranceIsolationScope.RequestNetwork &&
            configuration.Mode == KsProcgenEntranceConnectionMode.ExactComponents && !scene.HostContextComplete)
            return Fail(KsProcgenEntranceSceneStatus.Rejected, "IncompleteEntranceHostContext");
        var domain = scene.CleanDomainCells.ToHashSet();
        var combined = domain.Concat(scene.CleanHostCells).ToHashSet();
        var sealedPorts = configuration.SealedPorts.ToHashSet(StringComparer.Ordinal);
        var operational = shape.Entrances.Where(entrance => !sealedPorts.Contains(entrance.PortId)).ToArray();
        foreach (var entrance in shape.Entrances)
        {
            if (sealedPorts.Contains(entrance.PortId))
            {
                if (entrance.ThresholdCells.Any(cell => !scene.BlockedThresholdCells.Contains(cell)))
                    return Fail(KsProcgenEntranceSceneStatus.Rejected, "UnblockedSealedEntrance");
                continue;
            }
            if (entrance.InsideApproach.Any(cell => !domain.Contains(cell)) ||
                entrance.ThresholdCells.Concat(entrance.OutsideApproach).Any(cell => !combined.Contains(cell)))
                return Fail(KsProcgenEntranceSceneStatus.Rejected, "EntranceApproachUnavailable");
        }
        List<IReadOnlyList<Vector2i>>? Components(HashSet<Vector2i> cells)
        {
            var remaining = cells.ToHashSet();
            var result = new List<IReadOnlyList<Vector2i>>();
            foreach (var start in KsProcgenGeometry.SortCells(cells))
            {
                if (!remaining.Remove(start))
                    continue;
                var component = new List<Vector2i>();
                var queue = new Queue<Vector2i>();
                queue.Enqueue(start);
                while (queue.TryDequeue(out var cell))
                {
                    if (expanded >= maximumExpandedCells)
                        return null;
                    expanded++;
                    component.Add(cell);
                    foreach (var step in Steps)
                        if (remaining.Remove(cell + step))
                            queue.Enqueue(cell + step);
                }
                result.Add(KsProcgenGeometry.SortCells(component).ToList().AsReadOnly());
            }
            return result;
        }
        var localComponents = Components(domain);
        if (localComponents == null)
            return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "EntranceSceneExpansionBudget");
        var localByCell = new Dictionary<Vector2i, int>();
        for (var index = 0; index < localComponents.Count; index++)
        foreach (var cell in localComponents[index])
            localByCell.Add(cell, index);
        var needFullGraph = configuration.IsolationScope == KsProcgenEntranceIsolationScope.RequestNetwork ||
            connectivityPolicy == KsProcgenConnectivityPolicy.SingleNetwork;
        var scopeByCell = localByCell;
        List<IReadOnlyList<Vector2i>>? fullComponents = null;
        if (needFullGraph)
        {
            fullComponents = Components(combined);
            if (fullComponents == null)
                return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "EntranceSceneExpansionBudget");
            scopeByCell = new();
            for (var index = 0; index < fullComponents.Count; index++)
            foreach (var cell in fullComponents[index])
                scopeByCell.Add(cell, index);
        }
        var portWitnesses = new List<KsProcgenEntrancePortWitness>();
        foreach (var entrance in operational)
        {
            var localIds = entrance.InsideApproach.Select(cell => localByCell[cell]).Distinct().ToArray();
            if (localIds.Length != 1)
                return Fail(KsProcgenEntranceSceneStatus.Rejected, "SplitWideEntranceApproach");
            var globalComponent = scopeByCell[entrance.InsideApproach[0]];
            portWitnesses.Add(new(entrance.PortId, localIds[0],
                configuration.IsolationScope == KsProcgenEntranceIsolationScope.DomainLocal ? localIds[0] : globalComponent,
                needFullGraph ? globalComponent : null));
        }
        var byPort = portWitnesses.ToDictionary(port => port.PortId, StringComparer.Ordinal);
        var groupsByLocal = new Dictionary<int, List<string>>();
        var exactScopeOwners = new Dictionary<int, string>();
        foreach (var group in configuration.Groups)
        {
            var localIds = group.Ports.Select(port => byPort[port].LocalComponent).Distinct().ToArray();
            if (localIds.Length != 1)
                return Fail(KsProcgenEntranceSceneStatus.Rejected, "RequiredEntranceGroupDisconnected");
            var component = localIds[0];
            if (group.RootCells.Any(cell => !localByCell.TryGetValue(cell, out var rootComponent) || rootComponent != component))
                return Fail(KsProcgenEntranceSceneStatus.Rejected, "EntranceGroupRootUnreachable");
            if (!groupsByLocal.TryGetValue(component, out var ids))
                groupsByLocal.Add(component, ids = []);
            ids.Add(group.Id);
            if (configuration.Mode != KsProcgenEntranceConnectionMode.ExactComponents)
                continue;
            var scopeComponent = configuration.IsolationScope == KsProcgenEntranceIsolationScope.DomainLocal ? component :
                byPort[group.Ports[0]].ScopeComponent;
            if (!exactScopeOwners.TryAdd(scopeComponent, group.Id))
                return Fail(KsProcgenEntranceSceneStatus.Rejected, "ForbiddenEntranceGroupJoin");
        }
        if (localComponents.Count != groupsByLocal.Count)
            return Fail(KsProcgenEntranceSceneStatus.Rejected, "UnassignedEntranceFloor");
        if (connectivityPolicy == KsProcgenConnectivityPolicy.SingleNetwork &&
            (domain.Select(cell => scopeByCell[cell]).Distinct().Count() > 1 ||
                roots.Any(cell => !scopeByCell.ContainsKey(cell) || domain.Count == 0 || scopeByCell[cell] != scopeByCell[domain.First()])))
            return Fail(KsProcgenEntranceSceneStatus.Rejected, "GlobalEntranceNetworkDisconnected");
        if (roots.Any(cell => !localByCell.ContainsKey(cell)))
            return Fail(KsProcgenEntranceSceneStatus.Rejected, "EntranceRequestRootUnreachable");
        var witnesses = localComponents.Select((cells, index) => new KsProcgenEntranceComponentWitness(index, cells,
            groupsByLocal[index].OrderBy(id => id, StringComparer.Ordinal).ToList().AsReadOnly())).ToList().AsReadOnly();
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-entrance-scene-v1");
        hash.AddString(shape.EntranceContract!.ContractHash.ToString(CultureInfo.InvariantCulture));
        hash.AddString(configurationId);
        hash.AddInt(scene.HostContextComplete ? 1 : 0);
        void AddCells(IEnumerable<Vector2i> cells)
        {
            var ordered = KsProcgenGeometry.SortCells(cells);
            hash.AddInt(ordered.Count);
            foreach (var cell in ordered)
            {
                hash.AddInt(cell.X);
                hash.AddInt(cell.Y);
            }
        }
        AddCells(domain);
        AddCells(shape.TargetCells);
        AddCells(shape.EnvelopeCells);
        AddCells(shape.PreservedCells);
        AddCells(shape.VoidCells);
        hash.AddString(shape.ConstantContractHash.ToString(CultureInfo.InvariantCulture));
        AddCells(scene.CleanHostCells);
        AddCells(scene.BlockedThresholdCells);
        AddCells(roots);
        foreach (var entrance in shape.Entrances)
        {
            hash.AddString(entrance.PortId);
            AddCells(entrance.ThresholdCells);
            AddCells(entrance.InsideApproach);
            AddCells(entrance.OutsideApproach);
            hash.AddInt(entrance.InwardNormal.X);
            hash.AddInt(entrance.InwardNormal.Y);
        }
        return new()
        {
            Status = KsProcgenEntranceSceneStatus.Candidate, ConfigurationId = configurationId,
            Components = witnesses, Ports = portWitnesses.OrderBy(port => port.PortId, StringComparer.Ordinal).ToList().AsReadOnly(),
            HostComponents = fullComponents == null ? [] : fullComponents.AsReadOnly(),
            SealedPorts = configuration.SealedPorts, ExpandedCells = expanded, SceneHash = hash.Value,
        };
    }

    private static bool ValidCell(Vector2i cell) => Math.Abs((long) cell.X) <= 1_000_000 && Math.Abs((long) cell.Y) <= 1_000_000;
}
