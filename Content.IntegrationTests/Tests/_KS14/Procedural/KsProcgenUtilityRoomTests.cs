#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Atmos.Components;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenUtilityRoomAllocator))]
public sealed class KsProcgenUtilityRoomTests : KsProcgenGameTest
{
    [TestCase(3, 6, 1, 2)] [TestCase(6, 3, 2, 1)] [TestCase(6, 6, 2, 2)]
    [TestCase(1, 4, 1, 2)] [TestCase(4, 1, 2, 1)] [TestCase(4, 4, 2, 2)]
    public async Task GeneratedPartitionMaterialsFormAnInitializedNativeCloset(int width, int height, int roomWidth, int roomHeight)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var maps = Pair.Server.System<SharedMapSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var fixture = Build(manager, width, height, roomWidth, roomHeight);
            var utilityRegion = fixture.Themes.Regions.Single(region => region.UtilityRoom != null);
            var utility = utilityRegion.UtilityRoom!;
            Assert.That(utility.Size, Is.EqualTo(new Vector2i(roomWidth, roomHeight)).Or.EqualTo(new Vector2i(roomHeight, roomWidth)));
            Assert.That(utility.ReusedHullCells, Is.Not.Empty);
            Assert.That(utilityRegion.Theme.ThemeId, Is.EqualTo("KsProcgenElectricalCloset"));
            Assert.That(utilityRegion.Theme.Content!.Goals.CoherentContents, Is.True);
            Assert.That(utilityRegion.Theme.DominantEntityPackId, Is.EqualTo("KsProcgenElectricalSupplies"));
            Assert.That(utilityRegion.FloorCells, Is.EquivalentTo(utility.InteriorCells.Append(utility.Threshold)));
            var structure = KsProcgenGroupWindowPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes, [], emptyExterior: true);
            Assert.That(structure.Status, Is.EqualTo(KsProcgenGroupWindowStatus.Planned), structure.Issue?.Code);
            Assert.That(structure.Windows, Is.Empty);
            var materials = structure.SourceHull!.InteriorMaterials!;
            Assert.That(materials.Doors.Any(door => door.Cell == utility.Threshold), Is.True);
            var mapUid = maps.CreateMap(out var mapId, runMapInit: false); maps.SetPaused(mapId, true);
            try
            {
                var grid = maps.CreateGridEntity(mapId); grid.Comp.CanSplit = false;
                var definitions = Pair.Server.ResolveDependency<ITileDefinitionManager>();
                var tiles = materials.Tiles.ToDictionary(tile => tile.Cell, tile => tile.TileId);
                foreach (var wall in structure.RemainingWalls)
                {
                    var region = fixture.Themes.Regions.Single(region => region.Id == wall.RegionId);
                    Assert.That(KsProcgenSelectedContent.TryTile(manager, region.Theme, out var palette), Is.True);
                    tiles.TryAdd(wall.Cell, palette!.PrimaryTile);
                }
                foreach (var (cell, tileId) in tiles) maps.SetTile(grid, cell, new Tile(definitions[tileId].TileId));
                var walls = new Dictionary<Vector2i, EntityUid>();
                foreach (var wall in structure.RemainingWalls)
                    walls.Add(wall.Cell, SEntMan.SpawnEntity(wall.EntityId,
                        new EntityCoordinates(grid.Owner, (float) wall.Cell.X + 0.5f, (float) wall.Cell.Y + 0.5f)));
                var doors = materials.Doors.ToDictionary(door => door.Cell, door => SEntMan.SpawnEntity(door.EntityId,
                    new EntityCoordinates(grid.Owner, (float) door.Cell.X + 0.5f, (float) door.Cell.Y + 0.5f)));
                maps.InitializeMap(mapId, unpause: false);
                foreach (var cell in utility.EnclosureCells.Where(cell => cell != utility.Threshold))
                {
                    Assert.That(walls.ContainsKey(cell), Is.True, cell.ToString());
                    var wallUid = walls[cell];
                    Assert.That(SEntMan.GetComponent<TransformComponent>(wallUid).Anchored, Is.True);
                    Assert.That(SEntMan.GetComponent<FixturesComponent>(wallUid).Fixtures.Values.Any(fixture => fixture.Hard), Is.True);
                    Assert.That(SEntMan.GetComponent<AirtightComponent>(wallUid).AirBlocked, Is.True);
                    Assert.That(SEntMan.GetComponent<MetaDataComponent>(wallUid).EntityLifeStage, Is.EqualTo(EntityLifeStage.MapInitialized));
                }
                Assert.That(doors.ContainsKey(utility.Threshold), Is.True);
                Assert.That(SEntMan.GetComponent<TransformComponent>(doors[utility.Threshold]).Anchored, Is.True);
                Assert.That(utility.InteriorCells.All(cell => !walls.ContainsKey(cell) && !doors.ContainsKey(cell)), Is.True);
                Assert.That(tiles.ContainsKey(utility.OutsideApproach), Is.True);
                Assert.That(fixture.Partition.EngineAccessVerified || structure.GasClosureVerified, Is.False);
                // This proves native structural primitives, not powered door operation, a substation,
                // furnished closet access, electrical coverage or whole-generator certification.
            }
            finally { maps.DeleteMap(mapId); }
            Assert.That(SEntMan.EntityExists(mapUid), Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ClosedInterfacesToOtherRoomsDoNotMergeTheClosetDuringThemeAssignment()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var fixture = Build(manager, 7, 7, 1, 2, split: true);
            Assert.That(fixture.Themes.Regions.Count, Is.GreaterThanOrEqualTo(3));
            var utility = fixture.Themes.Regions.Single(region => region.UtilityRoom != null);
            Assert.That(utility.SourceZoneIds.Count, Is.EqualTo(1));
            Assert.That(fixture.Themes.Regions.Where(region => region.UtilityRoom == null).All(region => region.Theme.ThemeId == "KsProcgenSimpleOffice"), Is.True);
            Assert.That(utility.Theme.ThemeId, Is.EqualTo("KsProcgenElectricalCloset"));
            var materials = KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes);
            Assert.That(materials.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), materials.Issue?.Code);
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task MissingOrWrongFrozenClosetThemeCannotFallBackToAnOrdinaryRoom(bool wrong)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var fixture = Build(manager, 6, 6, 1, 2);
            Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "KsProcgenSimpleOffice", out var ordinary, out var issue), Is.True, issue?.Code);
            var rejected = KsProcgenGroupThemePlanner.Plan(manager, "KsProcgenSimpleOffice", 1, fixture.Partition,
                content: ordinary, utilityContent: wrong ? ordinary : null);
            Assert.That(rejected.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.ThemeRejected));
            Assert.That(rejected.Issue?.Code, Is.EqualTo("UtilityRoomThemeRequired")); Assert.That(rejected.Regions, Is.Empty);
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task MissingWallOrDoorIsRejectedByIndependentThemeAudit(bool door)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var fixture = Build(manager, 6, 6, 1, 2);
            var network = fixture.Partition.Regions.Single(); var partition = network.Partition;
            var utility = network.Fill.Zones.Single(zone => zone.UtilityRoom != null).UtilityRoom!;
            var changed = new KsProcgenPartitionResult
            {
                Status = partition.Status, FloorCells = partition.FloorCells,
                WallCells = door ? partition.WallCells : partition.WallCells.Where(cell => cell != utility.PartitionEnclosureCells.First(item => item != utility.Threshold)).ToArray(),
                DoorOpenings = door ? partition.DoorOpenings.Where(item => item.Threshold != utility.Threshold).ToArray() : partition.DoorOpenings,
                MergedZoneGroups = partition.MergedZoneGroups,
            };
            var rejected = KsProcgenThemeAssignmentPlanner.Plan(manager, "KsProcgenSimpleOffice", 1, network.Fill, changed);
            Assert.That(rejected.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.InvalidInput)); Assert.That(rejected.Regions, Is.Empty);
        });
    }

    private static (KsProcgenRequest Request, KsProcgenGroupPartitionResult Partition, KsProcgenThemeAssignmentResult Themes)
        Build(IPrototypeManager manager, int width, int height, int roomWidth, int roomHeight, bool split = false)
    {
        var cells = Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(y => new Vector2i(x, y))).ToList();
        var envelope = Enumerable.Range(-1, width + 2).SelectMany(x => Enumerable.Range(-1, height + 2)
            .Select(y => new Vector2i(x, y))).Where(cell => !cells.Contains(cell)).ToList();
        Assert.That(KsProcgenRequestSnapshot.TryCapture(new() { RequestId = "UtilityNative", EntranceDomain = new(), Shape = new() { Cells = cells, EnvelopeCells = envelope },
            WindowGoal = new() { ExteriorWindowFraction = 0f } }, out var snapshot, out var issue), Is.True, issue?.Code);
        var request = snapshot!.CopyRequest();
        var floor = KsProcgenGroupFloorPlanner.Plan(request, request.EntranceDomain!.Configurations.Single().Id,
            new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true));
        Assert.That(floor.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), floor.Issue?.Code);
        Assert.That(KsProcgenElectricalProfiles.TrySnapshot(new() { SchemaVersion = 2, Distribution = new()
            { ClosetInteriorSizes = new() { new(roomWidth, roomHeight) } } }, out var profile, out issue), Is.True, issue?.Code);
        var zones = split ? new[]
        {
            new KsProcgenZone("Left", KsProcgenZoneKind.RoomProposal, floor.FloorCells.Where(cell => cell.X < 3).ToArray()),
            new KsProcgenZone("Right", KsProcgenZoneKind.RoomProposal, floor.FloorCells.Where(cell => cell.X >= 3).ToArray()),
        } : null;
        var partition = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64, zones: zones,
            electricalDistribution: profile!.Distribution);
        Assert.That(partition.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), partition.Issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "KsProcgenSimpleOffice", out var ordinary, out issue), Is.True, issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "KsProcgenElectricalCloset", out var utility, out issue), Is.True, issue?.Code);
        var themes = KsProcgenGroupThemePlanner.Plan(manager, ordinary!.Theme.Id, 1, partition, content: ordinary, utilityContent: utility);
        Assert.That(themes.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.Selected), themes.Issue?.Code);
        return (request, partition, themes);
    }
}
