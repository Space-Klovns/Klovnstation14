using System.Linq;
using System.Threading;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.Power.NodeGroups;
using Content.Server.Power.Nodes;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.Map.Components;

namespace Content.Server.NodeContainer.EntitySystems;

/// <summary>One bounded topology operation, never a supply or full callback-isolation certificate.</summary>
public sealed record KsOwnedNodeTopologyResult(string? IssueCode, int Work, int NodeCount);

public sealed partial class NodeGroupSystem
{
    [Dependency] private SharedMapSystem _ksMaps = default!;
    // Synchronous scratch context only. Persistent per-map state belongs to the owning stage component.
    private KsOwnedNodeScope? _ksNodeScope;

    private IEnumerable<Node> GetCompatibleNodes(Node node) =>
        _ksNodeScope is { } scope ? scope.Adjacency[node] : GetCompatibleNodesCore(node);

    private sealed class KsOwnedNodeScope
    {
        public readonly HashSet<Node> Nodes = new();
        public readonly Dictionary<Node, Node[]> Adjacency = new();
        public readonly HashSet<BaseNodeGroup> Remake = new();
        public readonly List<Node> Reflood = new();
        public readonly HashSet<Node> Remove = new();
        public bool Processing;
        public bool CallbackDrift;
    }

    private void DoGroupUpdates()
    {
        var scope = _ksNodeScope;
        if (scope == null) { DoGroupUpdatesCore(); return; }
        if (scope.Processing) { scope.CallbackDrift = true; return; }
        scope.Processing = true;
        try { DoGroupUpdatesCore(); }
        finally { scope.Processing = false; }
    }

    private bool KsBlockImmediateCreation()
    {
        if (_ksNodeScope == null) return false;
        _ksNodeScope.CallbackDrift = true;
        return true;
    }

    private bool KsQueueReflood(Node node)
    {
        if (_ksNodeScope is not { } scope || !scope.Nodes.Contains(node)) return false;
        if (!node.FlaggedForFlood) { node.FlaggedForFlood = true; scope.Reflood.Add(node); }
        return true;
    }

    private bool KsQueueRemake(BaseNodeGroup group)
    {
        if (_ksNodeScope is not { } scope || !group.Nodes.Any(scope.Nodes.Contains)) return false;
        if (group.Nodes.Any(node => !scope.Nodes.Contains(node))) { scope.CallbackDrift = true; return true; }
        if (!group.Remaking)
        {
            group.Remaking = true; scope.Remake.Add(group);
            foreach (var node in group.Nodes) QueueReflood(node);
        }
        return true;
    }

    /// <summary>
    /// Rebuild power nodes on one initialized paused unit grid. The caller supplies the complete,
    /// already inspected owned scene and must retire it on any issue after mutation. Other grids'
    /// queues/groups are retained. Does not run the global power solver or promise rollback of
    /// arbitrary game callbacks. Unsupported nodes and cross-scope connections fail before mutation.
    /// </summary>
    internal KsOwnedNodeTopologyResult KsRebuildOwnedPowerNodes(EntityUid gridUid,
        IReadOnlyList<EntityUid> members, int maximumWork, CancellationToken cancellationToken = default) =>
        KsProcessOwnedPowerNodes(gridUid, members, maximumWork, true, cancellationToken);

    /// <summary>Inspect native topology without rebuilding groups, registering networks or draining queues.</summary>
    internal KsOwnedNodeTopologyResult KsInspectOwnedPowerNodes(EntityUid gridUid,
        IReadOnlyList<EntityUid> members, int maximumWork, CancellationToken cancellationToken = default) =>
        KsProcessOwnedPowerNodes(gridUid, members, maximumWork, false, cancellationToken);

    private KsOwnedNodeTopologyResult KsProcessOwnedPowerNodes(EntityUid gridUid,
        IReadOnlyList<EntityUid> members, int maximumWork, bool rebuild, CancellationToken cancellationToken)
    {
        var work = 0; var scope = new KsOwnedNodeScope();
        KsOwnedNodeTopologyResult Fail(string code) => new(code, work, scope.Nodes.Count);
        bool Spend(int amount = 1)
        {
            if (amount < 0 || amount > maximumWork - work) return false;
            work += amount; return true;
        }
        if (_ksNodeScope != null) { _ksNodeScope.CallbackDrift = true; return Fail("OwnedNodesOperationActive"); }
        if (members == null || members.Count > 4096 || maximumWork is < 0 or > 1_000_000)
            return Fail("InvalidOwnedNodeBudget");
        if (EntityManager.IsQueuedForDeletion(gridUid) || !TryComp<MapGridComponent>(gridUid, out var grid) || grid.TileSize != 1 || grid.CanSplit ||
            !_xformQuery.TryComp(gridUid, out var gridTransform) || gridTransform.MapUid is not { } mapUid ||
            EntityManager.IsQueuedForDeletion(mapUid) || !_ksMaps.TryGetMap(gridTransform.MapID, out var registeredMapUid) ||
            registeredMapUid != mapUid || gridTransform.ParentUid != mapUid || !_ksMaps.IsPaused(gridTransform.MapID) ||
            !_ksMaps.IsInitialized(gridTransform.MapID)) return Fail("OwnedNodesFrameRequired");
        var mapId = gridTransform.MapID;
        var gridPosition = gridTransform.LocalPosition; var gridRotation = gridTransform.LocalRotation;
        var snapshots = new List<(EntityUid Uid, TransformComponent Transform, System.Numerics.Vector2 Position,
            Angle Rotation, NodeContainerComponent Container, KeyValuePair<string, Node>[] Nodes)>();
        var owners = new HashSet<EntityUid>();
        foreach (var uid in members)
        {
            if (cancellationToken.IsCancellationRequested) return Fail("OwnedNodesCancelled");
            if (!Spend()) return Fail("OwnedNodesWorkBudget");
            if (!owners.Add(uid) || !Exists(uid) || EntityManager.IsQueuedForDeletion(uid)) return Fail("OwnedNodesMemberDrift");
            if (!_nodeContainerQuery.TryComp(uid, out var container)) continue;
            if (!_xformQuery.TryComp(uid, out var transform) || transform.GridUid != gridUid ||
                transform.MapUid != mapUid || transform.ParentUid != gridUid || !transform.Anchored || container.Nodes.Count > 64)
                return Fail("OwnedNodesMemberDrift");
            snapshots.Add((uid, transform, transform.LocalPosition, transform.LocalRotation, container, container.Nodes.ToArray()));
            foreach (var (name, node) in container.Nodes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!Spend()) return Fail("OwnedNodesWorkBudget");
                if (node.Owner != uid || node.Name != name || node.Deleting || _toRemove.Contains(node) ||
                    !scope.Nodes.Add(node)) return Fail("OwnedNodesMemberDrift");
                if (node.GetType() != typeof(CableNode) && node.GetType() != typeof(CableDeviceNode) &&
                    node.GetType() != typeof(CableTerminalNode) ||
                    node.NodeGroupID is not (NodeGroupID.HVPower or NodeGroupID.MVPower or NodeGroupID.Apc))
                    return Fail("OwnedNodesCapabilityUnsupported");
            }
        }
        // Include every member of existing groups; never split a shared/foreign group speculatively.
        foreach (var node in scope.Nodes)
        {
            if (node.NodeGroup == null) continue;
            if (node.NodeGroup is not (PowerNet or ApcNet) || ((BaseNodeGroup) node.NodeGroup).Removed)
                return Fail("OwnedNodesGroupUnsupported");
            if (!scope.Remake.Add((BaseNodeGroup) node.NodeGroup)) continue;
            foreach (var oldMember in node.NodeGroup.Nodes)
            {
                if (!Spend()) return Fail("OwnedNodesWorkBudget");
                if (!scope.Nodes.Contains(oldMember)) return Fail("OwnedNodesForeignGroup");
            }
        }
        foreach (var node in scope.Nodes)
        {
            if (cancellationToken.IsCancellationRequested) return Fail("OwnedNodesCancelled");
            if (!Spend()) return Fail("OwnedNodesWorkBudget");
            var adjacency = new List<Node>();
            foreach (var neighbor in GetCompatibleNodes(node))
            {
                if (!Spend()) return Fail("OwnedNodesWorkBudget");
                if (!scope.Nodes.Contains(neighbor)) return Fail("OwnedNodesForeignConnection");
                adjacency.Add(neighbor);
            }
            scope.Adjacency.Add(node, adjacency.ToArray());
        }
        // Reserve native graph/flood/group passes and the global-list partition scan before writes.
        // Callbacks and underlying engine iterators are not a fully metered CPU/serializer ledger.
        if ((rebuild && !Spend(amount: _toReflood.Count)) || !Spend(amount: scope.Nodes.Count * 8 +
                scope.Adjacency.Values.Sum(neighbors => neighbors.Length) * 4)) return Fail("OwnedNodesWorkBudget");
        var undirected = scope.Nodes.ToDictionary(node => node, _ => new HashSet<Node>());
        foreach (var (node, neighbors) in scope.Adjacency)
        foreach (var neighbor in neighbors) { undirected[node].Add(neighbor); undirected[neighbor].Add(node); }
        var components = new List<HashSet<Node>>(); var visited = new HashSet<Node>();
        foreach (var root in scope.Nodes)
        {
            if (!visited.Add(root)) continue;
            var component = new HashSet<Node> { root }; var pending = new Queue<Node>(); pending.Enqueue(root);
            while (pending.TryDequeue(out var node))
            foreach (var neighbor in undirected[node])
                if (visited.Add(neighbor)) { component.Add(neighbor); pending.Enqueue(neighbor); }
            components.Add(component);
        }
        string? VerifyCurrentTopology()
        {
            if (EntityManager.IsQueuedForDeletion(mapUid) || EntityManager.IsQueuedForDeletion(gridUid) ||
                !TryComp<MapGridComponent>(gridUid, out var currentGrid) || !ReferenceEquals(currentGrid, grid) ||
                currentGrid.TileSize != 1 || currentGrid.CanSplit ||
                !_ksMaps.TryGetMap(mapId, out var currentMapUid) || currentMapUid != mapUid ||
                !_xformQuery.TryComp(gridUid, out var currentGridTransform) || !ReferenceEquals(currentGridTransform, gridTransform) ||
                currentGridTransform.ParentUid != mapUid || currentGridTransform.MapUid != mapUid ||
                currentGridTransform.MapID != mapId ||
                currentGridTransform.LocalPosition != gridPosition || currentGridTransform.LocalRotation != gridRotation ||
                !_ksMaps.IsPaused(currentGridTransform.MapID) || !_ksMaps.IsInitialized(currentGridTransform.MapID))
                return "OwnedNodesFrameDrift";
            foreach (var snapshot in snapshots)
            {
                if (EntityManager.IsQueuedForDeletion(snapshot.Uid) || !_xformQuery.TryComp(snapshot.Uid, out var current) || !ReferenceEquals(current, snapshot.Transform) ||
                    current.ParentUid != gridUid || !current.Anchored || current.LocalPosition != snapshot.Position ||
                    current.GridUid != gridUid || current.MapUid != mapUid || current.MapID != mapId ||
                    current.LocalRotation != snapshot.Rotation || !_nodeContainerQuery.TryComp(snapshot.Uid, out var container) ||
                    !ReferenceEquals(container, snapshot.Container) || container.Nodes.Count != snapshot.Nodes.Length ||
                    snapshot.Nodes.Any(pair => !container.Nodes.TryGetValue(pair.Key, out var node) || !ReferenceEquals(node, pair.Value)))
                    return "OwnedNodesMemberDrift";
            }
            foreach (var node in scope.Nodes)
            {
                if (!scope.Adjacency[node].ToHashSet().SetEquals(GetCompatibleNodesCore(node)))
                    return "OwnedNodesTopologyDrift";
            }
            foreach (var component in components)
            {
                var root = component.First();
                if (root.NodeGroup is not BaseNodeGroup { Removed: false, Remaking: false } group ||
                    group.GroupId != root.NodeGroupID || group.Nodes.Count != component.Count ||
                    !component.SetEquals(group.Nodes) || component.Any(node => !ReferenceEquals(node.NodeGroup, group)))
                    return "OwnedNodesTopologyDrift";
            }
            return null;
        }
        if (cancellationToken.IsCancellationRequested) return Fail("OwnedNodesCancelled");
        if (!rebuild)
        {
            if (scope.Nodes.Any(node => node.FlaggedForFlood || _toRemove.Contains(node)) ||
                scope.Remake.Any(group => group.Remaking || _toRemake.Contains(group))) return Fail("OwnedNodesPendingRebuild");
            var issue = VerifyCurrentTopology();
            return issue == null ? new(null, work, scope.Nodes.Count) : Fail(issue);
        }
        foreach (var group in scope.Remake) { _toRemake.Remove(group); group.Remaking = true; }
        _toReflood.RemoveAll(scope.Nodes.Contains);
        foreach (var node in scope.Nodes) { node.FlaggedForFlood = true; scope.Reflood.Add(node); }
        _ksNodeScope = scope;
        try
        {
            DoGroupUpdates();
            if (cancellationToken.IsCancellationRequested) return Fail("OwnedNodesCancelled");
            if (scope.CallbackDrift || scope.Reflood.Count != 0 || scope.Remake.Count != 0 ||
                scope.Nodes.Any(node => node.Deleting || _toRemove.Contains(node))) return Fail("OwnedNodesCallbackDrift");
            var issue = VerifyCurrentTopology();
            if (issue != null) return Fail(issue);
            return new(null, work, scope.Nodes.Count);
        }
        catch (Exception) { return Fail("OwnedNodesEngineFailure"); }
        finally
        {
            _ksNodeScope = null;
            // Preserve callback/failure work for normal retirement/rebuild rather than dropping flags.
            foreach (var node in scope.Reflood) if (!_toReflood.Contains(node)) _toReflood.Add(node);
            foreach (var group in scope.Remake) _toRemake.Add(group);
        }
    }
}
