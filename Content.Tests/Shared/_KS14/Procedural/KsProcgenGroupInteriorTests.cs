using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGroupInteriorTests
{
    [Test]
    public void CompleteOwnedInteriorsReplayAndDetachTheirCleanScene()
    {
        var fixture = Fixture();
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Analysis!.Components.Count, Is.EqualTo(2));
        Assert.That(result.BlockedCells, Is.Empty);
        fixture.Furnishings.Reverse();
        fixture.Lighting.Reverse();
        var replay = Analyze(fixture);
        Assert.That(replay.AccessHash, Is.EqualTo(result.AccessHash));
        fixture.Request.Shape.Cells.Clear();
        Assert.That(result.Scene!.CleanDomainCells.Count, Is.EqualTo(24));
        Assert.That(result.EngineAccessVerified, Is.False);
    }

    [TestCase(KsProcgenMovementClass.Blocks, 1)]
    [TestCase(KsProcgenMovementClass.VaultRequired, 1)]
    [TestCase(KsProcgenMovementClass.Clear, 0)]
    public void MovementClassesDetermineCleanGraphBlockers(KsProcgenMovementClass movement, int blockedCount)
    {
        var fixture = Fixture();
        SetFurniture(fixture, new[] { Entity(new(2, 0), movement) });
        var result = Analyze(fixture);
        Assert.That(result.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), result.Issue?.Code);
        Assert.That(result.BlockedCells.Count, Is.EqualTo(blockedCount));
        Assert.That(result.Scene!.CleanDomainCells.Contains(new(2, 0)), Is.EqualTo(blockedCount == 0));
    }

    [Test]
    public void ProtectedEntrancesAndMachineApproachesCannotBecomeLightFootprints()
    {
        var fixture = Fixture();
        fixture.Lighting[0] = new(fixture.Lighting[0].RegionId, new()
        {
            Status = KsProcgenLightingPlanStatus.Proposed, Lights = new[] { Light(new(0, 0)) },
        });
        AssertFailure(Analyze(fixture), "OccupiedProtectedEntranceRoute", KsProcgenEntranceSceneStatus.Rejected);
        fixture = Fixture();
        SetFurniture(fixture, new[] { Entity(new(3, 0), KsProcgenMovementClass.Blocks) with { InteractionApproach = new(2, 0) } });
        fixture.Lighting[0] = new(fixture.Lighting[0].RegionId, new()
        {
            Status = KsProcgenLightingPlanStatus.Proposed, Lights = new[] { Light(new(2, 0)) },
        });
        AssertFailure(Analyze(fixture), "BlockedInteriorApproach", KsProcgenEntranceSceneStatus.Rejected);
    }

    [Test]
    public void AFullFurnitureCutCannotRelyOnAnotherRoomForAccess()
    {
        var fixture = Fixture();
        SetFurniture(fixture, new[] { Entity(new(2, 0), KsProcgenMovementClass.Blocks), Entity(new(2, 1), KsProcgenMovementClass.VaultRequired) });
        AssertFailure(Analyze(fixture), "DisconnectedInteriorRoom", KsProcgenEntranceSceneStatus.Rejected);
    }

    [Test]
    public void ForeignGroupMetadataMissingReservationsAndDuplicateProposalsRejectAtomically()
    {
        var fixture = Fixture();
        var regions = fixture.Themes.Regions.ToArray();
        regions[0] = regions[0] with { EntranceGroupIds = new[] { "B" } };
        var wrongOwnership = new KsProcgenThemeAssignmentResult { Status = KsProcgenThemeAssignmentStatus.Selected, Regions = regions };
        AssertFailure(KsProcgenGroupInteriorAnalyzer.Analyze(fixture.Request, fixture.Partition, wrongOwnership, fixture.Furnishings, fixture.Lighting),
            "InvalidInteriorOwnership", KsProcgenEntranceSceneStatus.InvalidInput);
        SetFurniture(fixture, new KsProcgenEntityProposal[0], reserve: false);
        AssertFailure(Analyze(fixture), "InvalidInteriorOwnership", KsProcgenEntranceSceneStatus.InvalidInput);
        fixture = Fixture();
        fixture.Lighting[1] = fixture.Lighting[0];
        AssertFailure(Analyze(fixture), "DuplicateInteriorRegion", KsProcgenEntranceSceneStatus.InvalidInput);
    }

    [Test]
    public void ForeignOrOverlappingEntityFootprintsAndUnexpectedLightsReject()
    {
        var fixture = Fixture();
        SetFurniture(fixture, new[] { Entity(new(2, 4), KsProcgenMovementClass.Clear) });
        AssertFailure(Analyze(fixture), "InvalidInteriorEntityFootprint", KsProcgenEntranceSceneStatus.InvalidInput);
        fixture = Fixture();
        SetFurniture(fixture, new[] { Entity(new(2, 0), KsProcgenMovementClass.Clear), Entity(new(2, 0), KsProcgenMovementClass.Clear) });
        AssertFailure(Analyze(fixture), "InvalidInteriorEntityFootprint", KsProcgenEntranceSceneStatus.InvalidInput);
        fixture = Fixture();
        fixture.Lighting[0] = new(fixture.Lighting[0].RegionId, new()
        {
            Status = KsProcgenLightingPlanStatus.NotRequested, Lights = new[] { Light(new(2, 0)) },
        });
        AssertFailure(Analyze(fixture), "UnexpectedInteriorLights", KsProcgenEntranceSceneStatus.InvalidInput);
    }

    [Test]
    public void ReadOnlyCleanFloorCannotGainEvenTraversibleEntityWrites()
    {
        var fixture = Fixture(preservedEntry: true);
        SetFurniture(fixture, new[] { Entity(new(0, 0), KsProcgenMovementClass.Clear) });
        AssertFailure(Analyze(fixture), "InvalidInteriorEntityFootprint", KsProcgenEntranceSceneStatus.InvalidInput);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void PartitionDoorThresholdAndBothApproachesStayClean(int part)
    {
        var fixture = Fixture(splitRooms: true);
        Assert.That(Analyze(fixture).Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        var door = fixture.Partition.Regions[0].Partition.DoorOpenings.Single();
        var cell = new[] { door.Threshold, door.InsideApproach, door.OutsideApproach }[part];
        var region = fixture.Themes.Regions.Single(room => room.FloorCells.Contains(cell));
        var index = fixture.Furnishings.FindIndex(room => room.RegionId == region.Id);
        fixture.Furnishings[index] = new(region.Id, new()
        {
            Status = KsProcgenFurnishingStatus.Proposed, ProtectedPassageCells = region.ProtectedPassageCells,
            Entities = new[] { Entity(cell, KsProcgenMovementClass.VaultRequired) },
        });
        AssertFailure(Analyze(fixture), "BlockedInteriorApproach", KsProcgenEntranceSceneStatus.Rejected);
    }

    [Test]
    public void SourceRoomAndFinalSceneChecksShareAnAtomicBudget()
    {
        var fixture = Fixture();
        var full = Analyze(fixture);
        var limited = KsProcgenGroupInteriorAnalyzer.Analyze(fixture.Request, fixture.Partition, fixture.Themes,
            fixture.Furnishings, fixture.Lighting, maximumExpandedCells: full.ExpandedCells - 1);
        AssertFailure(limited, "EntranceSceneExpansionBudget", KsProcgenEntranceSceneStatus.BudgetExceeded);
        Assert.That(limited.ExpandedCells, Is.EqualTo(full.ExpandedCells - 1));
        var exact = KsProcgenGroupInteriorAnalyzer.Analyze(fixture.Request, fixture.Partition, fixture.Themes,
            fixture.Furnishings, fixture.Lighting, maximumExpandedCells: full.ExpandedCells);
        Assert.That(exact.AccessHash, Is.EqualTo(full.AccessHash));
    }

    private sealed record InteriorFixture(KsProcgenRequest Request, KsProcgenGroupPartitionResult Partition,
        KsProcgenThemeAssignmentResult Themes, List<KsProcgenFurnishedRegion> Furnishings, List<KsProcgenLitRegion> Lighting);

    private static InteriorFixture Fixture(bool splitRooms = false, bool preservedEntry = false)
    {
        var request = new KsProcgenRequest
        {
            RequestId = "InteriorAudit", Seed = 23, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { Cells = Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToList() },
            EntranceDomain = new()
            {
                Entrances = new List<KsProcgenEntranceSpec>
                {
                    new() { PortId = "A1", ThresholdCells = new List<Vector2i> { new(-1, 0) }, InwardNormal = new(1, 0) },
                    new() { PortId = "B1", ThresholdCells = new List<Vector2i> { new(-1, 4) }, InwardNormal = new(1, 0) },
                },
                Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Separate", Groups = new List<KsProcgenEntranceGroupSpec>
                    { new() { Id = "A", Ports = new List<string> { "A1" } }, new() { Id = "B", Ports = new List<string> { "B1" } } } } },
            },
        };
        var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
            new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(-1, 4), new(-2, 4) }, new HashSet<Vector2i>(), true, true);
        if (preservedEntry)
        {
            request.Shape.PreservedCells = new List<Vector2i> { new(0, 0) };
            context = context with { ExistingCleanDomainCells = new HashSet<Vector2i> { new(0, 0) } };
        }
        var floor = KsProcgenGroupFloorPlanner.Plan(request, "Separate", context);
        var splitZones = splitRooms ? floor.Regions.SelectMany(network => new[]
        {
            new KsProcgenZone(network.Id + "/Left", KsProcgenZoneKind.RoomProposal, network.FloorCells.Where(cell => cell.X < 3).ToArray()),
            new KsProcgenZone(network.Id + "/Right", KsProcgenZoneKind.RoomProposal, network.FloorCells.Where(cell => cell.X >= 3).ToArray()),
        }).ToArray() : null;
        var partition = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64, zones: splitZones);
        var selection = new KsProcgenThemeSelection("Theme", "Tiles", "Palette", "Walls", "Family", null, null, null, new string[0]);
        var regions = partition.Regions.SelectMany(network => network.Fill.Zones.Select(zone => new KsProcgenThemedRegion(zone.Id, KsProcgenZoneKind.RoomProposal,
            new[] { zone.Id }, zone.Cells.Where(network.Partition.FloorCells.Contains).ToArray(), network.Partition.WallCells, selection, new KsProcgenPackMinimum[0])
        {
            EntranceGroupIds = network.GroupIds, ProtectedPassageCells = zone.Cells.Where(network.ProtectedCells.Contains).ToArray(),
        })).ToArray();
        return new(request, partition, new() { Status = KsProcgenThemeAssignmentStatus.Selected, Regions = regions },
            regions.Select(region => new KsProcgenFurnishedRegion(region.Id, new() { Status = KsProcgenFurnishingStatus.Proposed,
                ProtectedPassageCells = region.ProtectedPassageCells })).ToList(),
            regions.Select(region => new KsProcgenLitRegion(region.Id, new() { Status = KsProcgenLightingPlanStatus.NotRequested })).ToList());
    }
    private static KsProcgenEntityProposal Entity(Vector2i cell, KsProcgenMovementClass movement) =>
        new("Pack", "Entry", "Entity", KsProcgenEntityRole.Storage, movement, cell, 0, null);
    private static KsProcgenLightProposal Light(Vector2i cell) => new(cell, "Light", "Fixture", KsProcgenLightingSupply.SelfContained);
    private static void SetFurniture(InteriorFixture fixture, IReadOnlyList<KsProcgenEntityProposal> entities, bool reserve = true) =>
        fixture.Furnishings[0] = new(fixture.Furnishings[0].RegionId, new() { Status = KsProcgenFurnishingStatus.Proposed, Entities = entities,
            ProtectedPassageCells = reserve ? fixture.Themes.Regions[0].ProtectedPassageCells : new Vector2i[0] });
    private static KsProcgenGroupInteriorResult Analyze(InteriorFixture fixture) =>
        KsProcgenGroupInteriorAnalyzer.Analyze(fixture.Request, fixture.Partition, fixture.Themes, fixture.Furnishings, fixture.Lighting);
    private static void AssertFailure(KsProcgenGroupInteriorResult result, string code, KsProcgenEntranceSceneStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Issue?.Code, Is.EqualTo(code));
        Assert.That(result.Scene, Is.Null);
        Assert.That(result.Analysis, Is.Null);
        Assert.That(result.BlockedCells, Is.Empty);
        Assert.That(result.AccessHash, Is.Zero);
    }
}
