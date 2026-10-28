#nullable enable
using System;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    private static KsProcgenProjectSpec LayoutProjectSpec(KsProcgenProject project, byte[] bytes)
    {
        var spec = project.CopySpec(); var layouts = spec.Layouts!;
        layouts.Rooms["Room"].Cells = new() { new(0, 0) }; layouts.Rooms["Room"].SourceOrigin = new(1, 0);
        layouts.Rooms["Other"] = new() { SourceKey = "test-room", ContentFingerprint = Fingerprint(bytes), SourceOrigin = new(1, 0), Cells = new() { new(0, 0) } };
        var reservation = new System.Collections.Generic.List<Vector2i> { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };
        layouts.Families["Choice"] = new()
        {
            AllowedQuarterTurns = new() { 0, 1, 2, 3 },
            Options = new()
            {
                new() { Id = "L", ReservationCells = reservation, GenerateCells = new() { new(1, 1) }, Members = new()
                {
                    new() { Id = "A", Room = "Room", Origin = new(0, 0) },
                    new() { Id = "B", Room = "Room", Origin = new(1, 0), QuarterTurns = 1 },
                    new() { Id = "C", Room = "Room", Origin = new(0, 1), QuarterTurns = 2 },
                } },
                new() { Id = "Four", ReservationCells = reservation, Members = new()
                {
                    new() { Id = "A", Room = "Other", Origin = new(0, 0) },
                    new() { Id = "B", Room = "Other", Origin = new(1, 0), QuarterTurns = 1 },
                    new() { Id = "C", Room = "Other", Origin = new(0, 1), QuarterTurns = 2 },
                    new() { Id = "D", Room = "Other", Origin = new(1, 1), QuarterTurns = 3 },
                } },
            },
        };
        layouts.Libraries["Library"] = new() { Families = new() { "Choice" } };
        spec.Areas["Area"] = new() { Theme = "KsProcgenNoDoorThemeFixture", Library = "Library", Request = new()
        {
            SchemaVersion = 1, RequestId = "Area", Seed = 17, Mode = KsProcgenMode.Hybrid,
            Shape = new() { Cells = new() { new(5, -2), new(6, -2), new(5, -1), new(6, -1) } },
        } };
        return spec;
    }

    [TestCase(75, "L", 3)] [TestCase(100, "Four", 4)]
    public async Task AutomaticallySelectedNativeLayoutsCopyWholeAlternativesWithoutMixingTheirMembers(int coverage, string option, int roomCount)
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            root.Available = false;
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(Pair.Server.System<KsProcgenLayoutInstantiationSystem>().TryInstantiate(project, "Area", source, extraction!, gridUid,
                    allocations, 32, out var layout, out issue,
                    budgets: new() { Packing = new() { PreferredPrefabCoveragePercent = coverage } }), Is.True, message: issue?.ToString());
                Assert.That(layout!.Packing.Placements, Has.Count.EqualTo(1)); Assert.That(layout.Packing.Placements[0].OptionId, Is.EqualTo(option));
                Assert.That(layout.Rooms, Has.Count.EqualTo(roomCount)); Assert.That(layout.GeneratedCells, Has.Count.EqualTo(4 - roomCount));
                Assert.That(layout.Rooms.Select(room => room.OptionId).Distinct(), Is.EqualTo(new[] { option }));
                Assert.That(layout.Rooms.Select(room => room.AssetId).Distinct(), Is.EqualTo(new[] { option == "L" ? "Room" : "Other" }));
                Assert.That(layout.SourceTileProbes, Is.EqualTo(4 * roomCount));
                Assert.That(layout.Rooms.SelectMany(room => room.Instance.Members.Values).Distinct().Count(), Is.EqualTo(2 * roomCount));
                var maps = Pair.Server.System<SharedMapSystem>(); var grid = SEntMan.GetComponent<MapGridComponent>(gridUid);
                foreach (var room in layout.Rooms)
                {
                    var members = room.Instance.Members;
                    Assert.That(SEntMan.GetComponent<KsProcgenMapReferenceProbeComponent>(members[10]).Target, Is.EqualTo(members[2]));
                    var cells = layout.Packing.CellClaims.Where(entry => entry.Claim.OwnerId == room.OwnerId &&
                        entry.Claim.Disposition == KsProcgenCellDisposition.Prefab).Select(entry => entry.Cell);
                    Assert.That(room.Instance.Terrain.Tiles.Select(tile => tile.Cell), Is.EquivalentTo(cells));
                    foreach (var tile in room.Instance.Terrain.Tiles) Assert.That(maps.GetTileRef(gridUid, grid, tile.Cell).Tile, Is.EqualTo(tile.Tile));
                }
                foreach (var cell in layout.GeneratedCells) Assert.That(maps.GetTileRef(gridUid, grid, cell).Tile, Is.EqualTo(Tile.Empty));
            }, layFloor: false);
        }));
    }

    [TestCase("rooms", "LayoutInstantiationRoomBudget")]
    [TestCase("tiles", "RoomTerrainTileBudget")]
    [TestCase("probes", "RoomTerrainProbeBudget")]
    [TestCase("actors", "LayoutInstantiationAllocationBudget")]
    [TestCase("images", "LayoutInstantiationImageBudget")]
    public async Task NativeLayoutCopiesShareBudgetsAcrossEverySelectedRoomBeforeWriting(string kind, string expectedIssue)
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            var copier = Pair.Server.System<KsProcgenLayoutInstantiationSystem>(); var imageCost = 0;
            if (kind == "images") WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(copier.TryInstantiate(project, "Area", source, extraction!, gridUid, allocations, 32, out var measured, out issue,
                    budgets: new() { Packing = new() { PreferredPrefabCoveragePercent = 100 } }), Is.True, message: issue?.Code);
                imageCost = measured!.ImageCharacters;
            }, layFloor: false);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var budgets = new KsProcgenLayoutInstantiationBudgets
                {
                    Packing = new() { PreferredPrefabCoveragePercent = 100 }, MaximumRoomInstances = kind == "rooms" ? 3 : 256,
                    MaximumTerrainCells = kind == "tiles" ? 3 : 65_536, MaximumSourceTileProbes = kind == "probes" ? 15 : 262_144,
                    MaximumImageCharacters = kind == "images" ? imageCost - 1 : 4_194_304,
                };
                Assert.That(copier.TryInstantiate(project, "Area", source, extraction!, gridUid, allocations, kind == "actors" ? 9 : 32,
                    out var layout, out issue, budgets: budgets), Is.False);
                Assert.That(layout, Is.Null); Assert.That(issue?.Code, Is.EqualTo(expectedIssue));
                Assert.That(allocations.Count, Is.EqualTo(2)); Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
            }, layFloor: false);
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task LayoutMaterializationKeepsRevisionAndEntranceAwarePackingGuards(bool entranceContract)
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            var spec = LayoutProjectSpec(project, root.Bytes);
            if (entranceContract)
            { spec.Areas["Area"].Request.SchemaVersion = 2; spec.Areas["Area"].Request.EntranceDomain = new(); }
            project = Project(spec); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            if (!entranceContract)
            {
                Assert.That(project.TryRevise(Pair.Server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>(), project.CopySpec(),
                    out var revised, out issue), Is.True, message: issue?.Code);
                project = revised!;
            }
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                Assert.That(Pair.Server.System<KsProcgenLayoutInstantiationSystem>().TryInstantiate(project, "Area", source, extraction!, gridUid,
                    allocations, 32, out var layout, out issue), Is.False);
                Assert.That(layout, Is.Null); Assert.That(issue?.Code,
                    Is.EqualTo(entranceContract ? "UnsupportedEntranceAwarePacking" : "LayoutInstantiationSourceMismatch"));
                Assert.That(allocations.Count, Is.EqualTo(2)); Assert.That(SEntMan.GetComponent<MapGridComponent>(gridUid).ChunkCount, Is.Zero);
            }, layFloor: false);
        }));
    }

    [Test]
    public async Task LaterSelectedRoomStartupCannotInvalidateAnEarlierRoomCopy()
    {
        await AssertOnServer(() => WithSource(CopySource().Replace("- proto: Table", "- proto: ''"), (capture, project, bindings, root) =>
        {
            project = Project(LayoutProjectSpec(project, root.Bytes)); var source = Capture(capture, project, bindings);
            Assert.That(capture.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.Code);
            WithRoomCopyTransaction((gridUid, allocations) =>
            {
                var firstUid = EntityUid.Invalid; var calls = 0; var startup = Pair.Server.System<KsProcgenRoomCopyTestSystem>();
                startup.StartupAction = uid =>
                {
                    calls++;
                    if (calls == 1) firstUid = uid;
                    if (calls == 4) Pair.Server.System<SharedTransformSystem>().SetLocalPosition(firstUid,
                        SEntMan.GetComponent<TransformComponent>(firstUid).LocalPosition + System.Numerics.Vector2.UnitX);
                };
                try
                {
                    Assert.That(Pair.Server.System<KsProcgenLayoutInstantiationSystem>().TryInstantiate(project, "Area", source, extraction!, gridUid,
                        allocations, 32, out var layout, out issue,
                        budgets: new() { Packing = new() { PreferredPrefabCoveragePercent = 100 } }), Is.False);
                    Assert.That(calls, Is.EqualTo(4)); Assert.That(layout, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomInstantiationMemberDrift"));
                }
                finally { startup.StartupAction = null; }
            }, layFloor: false);
        }));
    }
}
