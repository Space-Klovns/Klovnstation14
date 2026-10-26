using System.Collections.Frozen;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed partial class KsProcgenElectricalPlanningContext
{
    private static readonly Vector2i[] Cardinal = [new(0, -1), new(-1, 0), new(1, 0), new(0, 1)];

    internal int HomeCount(KsProcgenThemeAssignmentResult themes)
    {
        var count = 1;
        foreach (var group in HomeGroups(themes))
        {
            if (group.Length == 0) return 0;
            count = (int) Math.Min(1_000_000L, (long) count * (long) group.Length);
        }
        return count;
    }

    private IReadOnlyList<KsProcgenThemedRegion[]> HomeGroups(KsProcgenThemeAssignmentResult themes) =>
        themes.Regions.GroupBy(region => string.Concat(region.EntranceGroupIds.Select(id => id.Length.ToString(CultureInfo.InvariantCulture) + ":" + id)), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Where(region => region.Kind == KsProcgenZoneKind.RoomProposal &&
                    (Profile.Distribution == null || region.UtilityRoom == null))
                .OrderByDescending(region => region.FloorCells.Count).ThenBy(region => region.Id, StringComparer.Ordinal).ToArray()).ToArray();

    internal bool TryInfrastructure(KsProcgenRequest request, KsProcgenNormalizedShape shape,
        KsProcgenThemeAssignmentResult themes, int homeOrdinal,
        out IReadOnlyDictionary<string, IReadOnlyList<KsProcgenRoomCoreSelection>> requirements, out KsProcgenIssue? issue)
    {
        var result = new Dictionary<string, IReadOnlyList<KsProcgenRoomCoreSelection>>(StringComparer.Ordinal);
        requirements = result; issue = null;
        var permitted = request.Utilities?.DeviceWriteCells?.ToHashSet();
        var cablePermitted = request.Utilities?.CableWriteCells?.ToHashSet();
        IReadOnlySet<Vector2i> Allowed(IEnumerable<Vector2i> candidates) => candidates.Where(cell => shape.CanWrite(cell) &&
            (permitted == null || permitted.Contains(cell)) && (cablePermitted == null || cablePermitted.Contains(cell))).ToFrozenSet();
        if (Profile.Distribution != null)
        {
            foreach (var network in themes.Regions.GroupBy(region => string.Concat(region.EntranceGroupIds.Select(id => id.Length.ToString(CultureInfo.InvariantCulture) + ":" + id)), StringComparer.Ordinal))
            {
                var closets = network.Where(region => region.UtilityRoom != null).ToArray();
                if (closets.Length != 1)
                { issue = new("ElectricalUtilityRoomRequired", "Each distribution network needs exactly one designated utility room."); return false; }
                var closet = closets[0]; var utility = closet.UtilityRoom!;
                if (!TryRequiredCores(closet, request.Seed, Allowed(utility.InteriorCells.Concat(utility.EnclosureCells)
                        .Where(cell => cell != utility.Threshold)), out var cores, out issue, substationRole: true)) return false;
                result.Add(closet.Id, cores);
            }
        }
        foreach (var group in HomeGroups(themes))
        {
            if (group.Length == 0) { issue = new("ElectricalDeviceRoomRequired", "Each generated network needs a room for converters."); return false; }
            var room = group[homeOrdinal % group.Length]; homeOrdinal /= group.Length;
            var allowed = Allowed(room.FloorCells.Concat(room.FloorCells.SelectMany(cell => Cardinal.Select(offset => cell + offset))).Distinct());
            if (!TryRequiredCores(room, request.Seed, allowed, out var cores, out issue,
                    substationRole: Profile.Distribution == null ? null : false)) return false;
            result.Add(room.Id, cores);
        }
        return true;
    }

    internal bool TryPlan(KsProcgenRequestSnapshot request, KsProcgenNormalizedShape shape,
        KsProcgenThemeAssignmentResult themes, KsProcgenGroupWindowResult structure,
        IReadOnlyList<KsProcgenFurnishedRegion> furnishings, KsProcgenGroupLightingResult lighting, int maximumWork,
        out KsProcgenElectricalPlan? plan, out KsProcgenIssue? issue, out int work)
    {
        var used = 0; plan = null; issue = null; work = 0;
        var error = "ElectricalRoutingRejected";
        bool Spend() { if (used >= maximumWork) { error = "ElectricalRoutingWorkBudget"; return false; } used++; return true; }
        var devices = new List<KsProcgenElectricalDevicePlacement>();
        var placements = new List<KsProcgenElectricalPlacement>();
        var omitted = new List<KsProcgenElectricalOmission>();
        var connections = new List<KsProcgenElectricalConnectionProposal>();
        KsProcgenLowVoltageRouter? lowRouter = null;
        KsProcgenElectricalServiceWitness? service = null;
        var cableSet = new HashSet<(Vector2i Cell, KsProcgenElectricalVoltage Voltage)>();
        var terrain = structure.SourceHull!.InteriorMaterials!.Tiles.Select(tile => tile.Cell)
            .Concat(structure.RemainingWalls.Select(item => item.Cell)).Concat(structure.Windows.Select(item => item.Cell))
            .Concat(structure.SourceHull.InteriorMaterials.Doors.Select(item => item.Cell)).ToHashSet();
        var cableMask = request.Utilities?.CableWriteCells?.ToHashSet();
        var allowed = terrain.Where(cell => shape.CanWrite(cell) && (cableMask == null || cableMask.Contains(cell))).ToFrozenSet();
        var orderedCells = KsProcgenGeometry.SortCells(allowed);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        KsProcgenElectricalCable[] Cables() => cableSet.Select(item => new KsProcgenElectricalCable(
            item.Voltage == KsProcgenElectricalVoltage.High ? Profile.HighVoltageCable :
            item.Voltage == KsProcgenElectricalVoltage.Medium ? Profile.MediumVoltageCable : Profile.LowVoltageCable,
            item.Cell, item.Voltage)).ToArray();
        bool AddPlacement(string id, string regionId, string prototypeId, Vector2 position, KsProcgenPlacementLayer layer)
        {
            if (!Spend()) return false;
            if (!_facts.ContainsKey(prototypeId)) { error = "UncapturedElectricalPrototype"; return false; }
            if (!ids.Add(id)) { error = "DuplicateElectricalMember"; return false; }
            placements.Add(new(id, regionId, prototypeId, position, layer)); return true;
        }
        bool Collect()
        {
            foreach (var region in furnishings)
            {
                if (region.Proposal.Layered is { } layered)
                {
                    var bindings = layered.Bindings.ToDictionary(item => item.MemberId, StringComparer.Ordinal);
                    foreach (var pose in layered.Access.Poses!.Members)
                    {
                        var binding = bindings[pose.Support.MemberId]; var id = region.RegionId + "/" + binding.MemberId;
                        if (binding.Origin == KsProcgenRoomMemberOrigin.Infrastructure)
                        {
                            if (!Spend()) return false;
                            if (pose.Support.Layer is not (KsProcgenPlacementLayer.Floor or KsProcgenPlacementLayer.Wall) || binding.CoreId is not ("Apc" or "Substation"))
                            { error = "InvalidElectricalConverterBinding"; return false; }
                            var cell = new Vector2i((int) Math.Floor((double) pose.Position.X), (int) Math.Floor((double) pose.Position.Y));
                            if (!shape.CanWrite(cell) || request.Utilities?.DeviceWriteCells is { } mask && !mask.Contains(cell))
                            { error = "ElectricalDeviceWritePermissionRequired"; return false; }
                            if (_facts[pose.Support.EntityPrototypeId].WallMounted != (pose.Support.Layer == KsProcgenPlacementLayer.Wall) ||
                                pose.Support.Layer == KsProcgenPlacementLayer.Wall && !structure.RemainingWalls.Any(wall => wall.Cell == cell))
                            { error = "ElectricalWallMountSupportRequired"; return false; }
                            devices.Add(new(id, region.RegionId, pose.Support.EntityPrototypeId, cell, pose.QuarterTurns, binding.CoreId == "Substation", pose.Support.Layer == KsProcgenPlacementLayer.Wall));
                        }
                        else if (!AddPlacement(id, region.RegionId, pose.Support.EntityPrototypeId, pose.Position, pose.Support.Layer)) return false;
                    }
                }
                else
                for (var index = 0; index < region.Proposal.Entities.Count; index++)
                {
                    var entity = region.Proposal.Entities[index];
                    if (!AddPlacement(region.RegionId + "/F/" + index.ToString(CultureInfo.InvariantCulture), region.RegionId, entity.EntityId,
                            Center(entity.Cell), KsProcgenPlacementLayer.Floor)) return false;
                }
            }
            foreach (var region in lighting.Regions)
            for (var index = 0; index < region.Proposal.Lights.Count; index++)
            {
                var light = region.Proposal.Lights[index];
                if (!AddPlacement(region.RegionId + "/L/" + index.ToString(CultureInfo.InvariantCulture), region.RegionId, light.EntityId,
                        Center(light.Cell), KsProcgenPlacementLayer.Floor)) return false;
            }
            foreach (var door in structure.SourceHull!.InteriorMaterials!.Doors)
                if (!AddPlacement(door.RegionId + "/D/" + door.Cell.X.ToString(CultureInfo.InvariantCulture) + "/" + door.Cell.Y.ToString(CultureInfo.InvariantCulture),
                        door.RegionId, door.EntityId, Center(door.Cell), KsProcgenPlacementLayer.Floor)) return false;
            return true;
        }
        bool AddCable(Vector2i cell, KsProcgenElectricalVoltage voltage)
        {
            if (!Spend()) return false;
            if (!allowed.Contains(cell)) { error = "ElectricalCableWritePermissionRequired"; return false; }
            cableSet.Add((cell, voltage)); return true;
        }
        bool Join(HashSet<Vector2i> tree, IReadOnlySet<Vector2i> targets, KsProcgenElectricalVoltage voltage)
        {
            if (voltage == KsProcgenElectricalVoltage.Low && lowRouter != null)
            {
                if (!lowRouter.TryJoin(targets, out var routeIssue)) { error = routeIssue!; return false; }
                foreach (var cell in lowRouter.Cells)
                {
                    if (!cableSet.Contains((cell, voltage)) && !AddCable(cell, voltage)) return false;
                    tree.Add(cell);
                }
                return true;
            }
            var queue = new Queue<Vector2i>(); var previous = new Dictionary<Vector2i, Vector2i>();
            foreach (var cell in KsProcgenGeometry.SortCells(tree)) { if (!Spend()) return false; previous.Add(cell, cell); queue.Enqueue(cell); }
            while (queue.TryDequeue(out var cell))
            {
                if (!Spend()) return false;
                if (targets.Contains(cell))
                {
                    while (true)
                    {
                        if (!AddCable(cell, voltage)) return false;
                        tree.Add(cell); var parent = previous[cell]; if (parent == cell) break; cell = parent;
                    }
                    return true;
                }
                foreach (var offset in Cardinal)
                {
                    if (!Spend()) return false;
                    var neighbor = cell + offset;
                    if (allowed.Contains(neighbor) && previous.TryAdd(neighbor, cell)) queue.Enqueue(neighbor);
                }
            }
            error = "ElectricalCableRouteUnavailable"; return false;
        }
        bool Build()
        {
            if (maximumWork is < 0 or > 1_000_000 || !Collect()) return false;
            if (devices.Count == 0 || devices.Count > 256) { error = "ElectricalConvertersMissing"; return false; }
            var regions = themes.Regions.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
            var consumers = request.Utilities?.Consumers ?? [];
            var matched = new Dictionary<string, List<KsProcgenElectricalConsumer>>(StringComparer.Ordinal);
            foreach (var consumer in consumers)
            {
                if (!Spend()) return false;
                if (consumer.Scope == KsProcgenUtilityScope.GasDomain)
                { error = "ElectricalGasDomainScopeUnsupported"; return false; }
                if (consumer.Scope == KsProcgenUtilityScope.Room && !regions.Contains(consumer.ScopeId!))
                { error = "UnknownElectricalConsumerRoom"; return false; }
                var matches = new List<KsProcgenElectricalPlacement>();
                foreach (var placement in placements)
                {
                    if (!Spend()) return false;
                    if (consumer.Scope == KsProcgenUtilityScope.Room && consumer.ScopeId != placement.RegionId) continue;
                    if (consumer.Selector == KsProcgenConsumerSelector.AllPowered && !_facts[placement.PrototypeId].Powered ||
                        consumer.Selector == KsProcgenConsumerSelector.Prototype && consumer.SelectorId != placement.PrototypeId ||
                        consumer.Selector == KsProcgenConsumerSelector.Member && consumer.SelectorId != placement.Id) continue;
                    matches.Add(placement);
                }
                if (matches.Count < consumer.MinimumMatches)
                {
                    if (consumer.Requirement == KsProcgenUtilityRequirement.Required) { error = "ElectricalConsumerMinimumUnmet"; return false; }
                    omitted.Add(new(consumer.Id, "PreferredElectricalConsumerMinimumUnmet"));
                }
                foreach (var placement in matches)
                {
                    if (!matched.TryGetValue(placement.Id, out var selected)) matched.Add(placement.Id, selected = new());
                    selected.Add(consumer);
                }
            }
            var medium = devices.Where(item => item.Substation).Select(item => item.Cell).ToHashSet();
            var low = devices.Where(item => !item.Substation).Select(item => item.Cell).ToHashSet();
            foreach (var device in devices)
            {
                if (!AddCable(device.Cell, device.Substation ? KsProcgenElectricalVoltage.High : KsProcgenElectricalVoltage.Low) ||
                    !AddCable(device.Cell, KsProcgenElectricalVoltage.Medium)) return false;
            }
            if (Profile.Distribution is { } distribution &&
                !KsProcgenLowVoltageRouter.TryCreate(allowed, low, distribution.LowVoltageSquares, Spend, out lowRouter, out var routerIssue))
            { error = routerIssue!; return false; }
            foreach (var apc in devices.Where(item => !item.Substation).OrderBy(item => item.Id, StringComparer.Ordinal))
                if (!Join(medium, new[] { apc.Cell }.ToFrozenSet(), KsProcgenElectricalVoltage.Medium)) return false;
            foreach (var placement in placements.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                if (!Spend()) return false;
                var fact = _facts[placement.PrototypeId]; matched.TryGetValue(placement.Id, out var declarations); declarations ??= new();
                if (!fact.Powered && declarations.Count == 0) continue;
                var required = declarations.Any(item => item.Requirement == KsProcgenUtilityRequirement.Required);
                if (declarations.Any(item => item.CircuitId != "Main")) { error = "ElectricalCircuitBindingUnsupported"; return false; }
                foreach (var declaration in declarations.Where(item => item.Supply == KsProcgenElectricalSupply.Operational))
                {
                    if (declaration.Requirement == KsProcgenUtilityRequirement.Required) { error = "ElectricalSupplyObservationRequired"; return false; }
                    omitted.Add(new(declaration.Id, "PreferredElectricalSupplyUnverified"));
                }
                if (!fact.Powered || !fact.HasReceiver || !fact.Anchored ||
                    placement.Layer != KsProcgenPlacementLayer.Floor && (Profile.Distribution == null || placement.Layer != KsProcgenPlacementLayer.Wall))
                {
                    if (required) { error = "ElectricalReceiverIneligible"; return false; }
                    omitted.Add(new(placement.Id, "ElectricalReceiverIneligible")); continue;
                }
                var watts = declarations.Where(item => item.NominalWatts.HasValue).Select(item => item.NominalWatts!.Value).Distinct().ToArray();
                var peaks = declarations.Where(item => item.PeakWatts.HasValue).Select(item => item.PeakWatts!.Value).Distinct().ToArray();
                if (watts.Length > 1 || peaks.Length > 1) { error = "ConflictingElectricalConsumerLoads"; return false; }
                var nominalWatts = watts.Length == 0 ? fact.NominalWatts : watts[0];
                var peakWatts = peaks.Length == 0 ? (float?) null : peaks[0];
                if (nominalWatts.HasValue && peakWatts.HasValue && peakWatts.Value < nominalWatts.Value)
                { error = "ConflictingElectricalConsumerLoads"; return false; }
                var targets = new HashSet<Vector2i>();
                foreach (var cell in orderedCells)
                {
                    if (!Spend()) return false;
                    if (KsProcgenElectricalReach.WithinRange(Center(cell), placement.Position, ProviderRange, fact.ReceptionRange)) targets.Add(cell);
                }
                if (!Join(low, targets, KsProcgenElectricalVoltage.Low))
                {
                    if (error == "ElectricalRoutingWorkBudget" || required) return false;
                    omitted.Add(new(placement.Id, error)); continue;
                }
                var provider = KsProcgenGeometry.SortCells(low.Where(targets.Contains)).First();
                connections.Add(new(placement.Id, placement.RegionId, placement.PrototypeId, placement.Position, provider,
                    nominalWatts, peakWatts));
            }
            if (Profile.Distribution is { } policy)
            {
                var mediumTargets = devices.Select(device => device.Cell).ToHashSet();
                foreach (var region in themes.Regions)
                {
                    if (!Spend()) return false;
                    if (policy.MediumVoltageCirculationBackbone)
                    {
                        foreach (var cell in region.ProtectedPassageCells.Concat(region.Kind == KsProcgenZoneKind.Passage ? region.FloorCells : []))
                        { if (!Spend()) return false; mediumTargets.Add(cell); }
                        if (region.UtilityRoom is { } room)
                            mediumTargets.UnionWith(new[] { room.InsideApproach, room.Threshold, room.OutsideApproach });
                    }
                    if (!policy.MediumVoltageRoomEntryBranches || region.Kind != KsProcgenZoneKind.RoomProposal) continue;
                    // A room without a separate corridor still receives explicit service trunks.
                    var xs = region.FloorCells.Select(cell => cell.X).OrderBy(value => value).ToArray();
                    var ys = region.FloorCells.Select(cell => cell.Y).OrderBy(value => value).ToArray();
                    foreach (var cell in region.FloorCells)
                    { if (!Spend()) return false; if (cell.X == xs[xs.Length / 2] || cell.Y == ys[ys.Length / 2]) mediumTargets.Add(cell); }
                }
                if (policy.MediumVoltageRoomEntryBranches)
                foreach (var door in structure.SourceHull!.InteriorMaterials!.DoorOpenings)
                { if (!Spend()) return false; mediumTargets.UnionWith(new[] { door.InsideApproach, door.Threshold, door.OutsideApproach }); }
                foreach (var target in KsProcgenGeometry.SortCells(mediumTargets))
                {
                    if (!allowed.Contains(target)) { error = "ElectricalMediumServicePermissionRequired"; return false; }
                    if (!Join(medium, new[] { target }.ToFrozenSet(), KsProcgenElectricalVoltage.Medium)) return false;
                }
                var blocked = furnishings.SelectMany(room => (IEnumerable<Vector2i>?) room.Proposal.Layered?.BlockedCells ?? room.Proposal.Entities
                    .Where(entity => entity.Movement != KsProcgenMovementClass.Clear).SelectMany(entity => entity.OccupiedCells)).ToHashSet();
                var domain = KsProcgenGeometry.SortCells(themes.Regions.Where(region => region.Kind == KsProcgenZoneKind.RoomProposal)
                    .SelectMany(region => region.FloorCells).Where(cell => shape.CanWrite(cell) &&
                        (policy.FloorServiceDomain == KsProcgenElectricalFloorDomain.AllRoomFloor || !blocked.Contains(cell))).Distinct());
                var required = (int) Math.Ceiling((double) ((float) domain.Count * policy.FloorServiceFraction));
                bool Coverage(out HashSet<Vector2i> covered)
                {
                    covered = new();
                    foreach (var cell in domain)
                    foreach (var provider in low)
                    {
                        if (!Spend()) return false;
                        if (!KsProcgenElectricalReach.WithinRange(Center(provider), Center(cell), ProviderRange, ProviderRange)) continue;
                        covered.Add(cell); break;
                    }
                    return true;
                }
                if (!Coverage(out var covered)) return false;
                foreach (var cell in domain)
                {
                    if (covered.Count >= required) break;
                    if (covered.Contains(cell)) continue;
                    var targets = new HashSet<Vector2i>();
                    foreach (var candidate in orderedCells)
                    {
                        if (!Spend()) return false;
                        if (KsProcgenElectricalReach.WithinRange(Center(candidate), Center(cell), ProviderRange, ProviderRange)) targets.Add(candidate);
                    }
                    if (!Join(low, targets, KsProcgenElectricalVoltage.Low))
                    { if (error.EndsWith("Budget", StringComparison.Ordinal)) return false; continue; }
                    if (!Coverage(out covered)) return false;
                }
                if (covered.Count < required) { error = "ElectricalFloorServiceUnmet"; return false; }
                if (!lowRouter!.TrySquareProofs(out var proofs, out var proofIssue)) { error = proofIssue!; return false; }
                service = new(allowed, mediumTargets, domain, covered, required, ProviderRange, proofs,
                    themes.Regions.Where(region => region.UtilityRoom != null).Select(region => new KsProcgenElectricalUtilityRoom(region.Id, region.UtilityRoom!)));
                if (!service.Validate(policy, Cables(), devices, Spend, out var serviceIssue))
                { error = serviceIssue!; return false; }
            }
            return true;
        }
        if (!Build()) { work = used; issue = new(error, "Complete owned electrical reservations could not validate."); return false; }
        work = used; plan = new(Profile, DeclarationHash, request.DeclarationHash, devices, Cables(), connections, omitted, used, service: service); return true;
    }

    private static Vector2 Center(Vector2i cell) => new((float) cell.X + 0.5f, (float) cell.Y + 0.5f);
}
