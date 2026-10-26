using System.Linq;
using System.Globalization;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using Content.Shared.Wall;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenOwnedStageSystem
{
    private string? Compile(KsProcgenNormalizedShape shape, KsProcgenOwnedInteriorResult proposal, KsProcgenElectricalProfile? electricalProfile,
        out KsProcgenOwnedScene? scene)
    {
        scene = null;
        if (proposal.Status != KsProcgenOwnedPlanStatus.Candidate || proposal.SemanticHash == 0 ||
            proposal.Access?.Status != KsProcgenEntranceSceneStatus.Candidate || proposal.Themes == null ||
            proposal.Partition == null || proposal.Lighting == null ||
            proposal.Structure?.SourceHull?.InteriorMaterials is not { } materials)
            return "OwnedStageIncompleteProposal";
        if ((electricalProfile?.Mode == KsProcgenElectricalMode.AutoDistribute) != (proposal.Electrical != null) ||
            proposal.Electrical != null && electricalProfile?.SemanticHash != proposal.Electrical.Profile.SemanticHash)
            return "OwnedStageElectricalProfileDrift";
        var tiles = new Dictionary<Vector2i, (string PrototypeId, ushort TileId)>();
        var entities = new List<KsProcgenOwnedSceneEntity>();
        var assemblies = new List<KsProcgenOwnedSceneAssembly>();
        var regions = proposal.Themes.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        string? AddTile(Vector2i cell, string id)
        {
            if (!shape.CanWrite(cell) || !_tileDefinitions.TryGetDefinition(id, out var tile) || tile.TileId == 0 ||
                !tiles.TryAdd(cell, (id, tile.TileId))) return "OwnedStageInvalidTile";
            return null;
        }
        string? AddStructure(Vector2i cell, string regionId, string prototypeId, int quarterTurns,
            KsProcgenOwnedSceneEntityKind kind, string? electricalMemberId = null)
        {
            if (!regions.TryGetValue(regionId, out var region) || !shape.CanWrite(cell) ||
                !ProtoMan.TryIndex<EntityPrototype>(prototypeId, out _)) return "OwnedStageInvalidStructure";
            // Structural cells without a floor choice receive this room's captured primary palette.
            // Door thresholds already have an exact floor choice, which must be retained.
            if (!tiles.ContainsKey(cell))
            {
                if (!KsProcgenSelectedContent.TryTile(ProtoMan, region.Theme, out var palette)) return "OwnedStageMissingPalette";
                var issue = AddTile(cell, palette.PrimaryTile);
                if (issue != null) return issue;
            }
            entities.Add(new(prototypeId, Center(cell), quarterTurns, kind) { ElectricalMemberId = electricalMemberId });
            return null;
        }
        foreach (var tile in materials.Tiles)
        {
            var issue = AddTile(tile.Cell, tile.TileId);
            if (issue != null) return issue;
        }
        var structureCells = new HashSet<Vector2i>();
        foreach (var wall in proposal.Structure.RemainingWalls)
        {
            if (!structureCells.Add(wall.Cell)) return "OwnedStageDuplicateStructure";
            var issue = AddStructure(wall.Cell, wall.RegionId, wall.EntityId, 0, KsProcgenOwnedSceneEntityKind.Wall);
            if (issue != null) return issue;
        }
        foreach (var window in proposal.Structure.Windows)
        {
            if (!structureCells.Add(window.Cell)) return "OwnedStageDuplicateStructure";
            var issue = AddStructure(window.Cell, window.RegionId, window.EntityId, 0, KsProcgenOwnedSceneEntityKind.Window);
            if (issue != null) return issue;
        }
        var openings = materials.DoorOpenings.ToDictionary(door => door.Threshold);
        foreach (var door in materials.Doors)
        {
            if (!structureCells.Add(door.Cell) || !openings.TryGetValue(door.Cell, out var opening)) return "OwnedStageInvalidDoor";
            // Default door orientation admits north/south approaches; east/west is a quarter turn.
            var issue = AddStructure(door.Cell, door.RegionId, door.EntityId,
                opening.InsideApproach.X == opening.Threshold.X ? 0 : 1, KsProcgenOwnedSceneEntityKind.Door,
                electricalMemberId: door.RegionId + "/D/" + door.Cell.X.ToString(CultureInfo.InvariantCulture) + "/" + door.Cell.Y.ToString(CultureInfo.InvariantCulture));
            if (issue != null) return issue;
        }
        foreach (var region in proposal.Furnishings)
        {
            if (region.Proposal.Layered is { } layered)
            {
                // Keep each independent room composition and all hidden/supported dependency poses.
                // FloorEntities is only a projection for path planning, never a second spawn list.
                assemblies.Add(new(region.RegionId, layered.Access.Poses!) { Bindings = layered.Bindings });
                continue;
            }
            for (var entityIndex = 0; entityIndex < region.Proposal.Entities.Count; entityIndex++)
            {
                var entity = region.Proposal.Entities[entityIndex];
                if (!tiles.ContainsKey(entity.Cell) || !ProtoMan.TryIndex<EntityPrototype>(entity.EntityId, out var prototype))
                    return "OwnedStageInvalidFurnishing";
                if (prototype.TryComp<WallMountComponent>(out _, Factory))
                    return "OwnedStageFlatWallMountUnsupported";
                entities.Add(new(entity.EntityId, Center(entity.Cell), entity.QuarterTurns, KsProcgenOwnedSceneEntityKind.Furnishing)
                    { ElectricalMemberId = region.RegionId + "/F/" + entityIndex.ToString(CultureInfo.InvariantCulture) });
            }
        }
        foreach (var region in proposal.Lighting.Regions)
        for (var lightIndex = 0; lightIndex < region.Proposal.Lights.Count; lightIndex++)
        {
            var light = region.Proposal.Lights[lightIndex];
            if (!tiles.ContainsKey(light.Cell) || !ProtoMan.TryIndex<EntityPrototype>(light.EntityId, out _))
                return "OwnedStageInvalidLight";
            entities.Add(new(light.EntityId, Center(light.Cell), 0, KsProcgenOwnedSceneEntityKind.Light)
                { ElectricalMemberId = region.RegionId + "/L/" + lightIndex.ToString(CultureInfo.InvariantCulture) });
        }
        if (proposal.Electrical != null)
        foreach (var cable in proposal.Electrical.Cables)
        {
            if (!shape.CanWrite(cable.Cell) || !tiles.ContainsKey(cable.Cell) || !ProtoMan.HasIndex<EntityPrototype>(cable.PrototypeId))
                return "OwnedStageInvalidCable";
            entities.Add(new(cable.PrototypeId, Center(cable.Cell), 0, KsProcgenOwnedSceneEntityKind.Cable) { Voltage = cable.Voltage });
        }
        var expected = proposal.Partition.FloorCells.Concat(proposal.Partition.WallCells)
            .Concat(proposal.Structure.RemainingWalls.Select(wall => wall.Cell))
            .Concat(proposal.Structure.Windows.Select(window => window.Cell)).ToHashSet();
        if (!expected.SetEquals(tiles.Keys)) return "OwnedStageIncompleteTerrain";
        scene = new(tiles.OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X)
            .Select(pair => new KsProcgenOwnedSceneTile(pair.Key, pair.Value.TileId, pair.Value.PrototypeId)), entities, assemblies,
            proposal.Partition.FloorCells, proposal.Furnishings.SelectMany(region => region.Proposal.Layered is { } layered
                ? layered.BlockedCells : region.Proposal.Entities.Where(entity => entity.Movement != KsProcgenMovementClass.Clear)
                    .SelectMany(entity => entity.OccupiedCells)), electricalLayout: proposal.Electrical?.Profile.Distribution != null
                ? new(proposal.Themes.Regions, materials.DoorOpenings) : null, electricalProfile: electricalProfile);
        return null;
    }

    private static Vector2 Center(Vector2i cell) => new(cell.X + 0.5f, cell.Y + 0.5f);
}
