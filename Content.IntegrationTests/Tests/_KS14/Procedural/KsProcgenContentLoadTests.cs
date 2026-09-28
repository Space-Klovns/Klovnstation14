using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenGeometry))]
public sealed class KsProcgenContentLoadTests : GameTest
{
    [Test]
    public async Task TinyShapeCanBeNormalizedInLoadedServerContent()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var request = new KsProcgenRequest
            {
                RequestId = "LoadedTiny",
                Shape = new KsProcgenShapeSpec
                {
                    Cells =
                    [
                        new Vector2i(3, 4),
                        new Vector2i(3, 5),
                        new Vector2i(3, 6),
                    ],
                },
            };

            Assert.That(KsProcgenGeometry.TryNormalize(request, out var shape, out var issue), Is.True, issue?.Message);
            Assert.That(shape.TargetComponents().Count, Is.EqualTo(1));
            Assert.That(shape.TargetCells.Count, Is.EqualTo(3));

            var plan = KsProcgenPackingPlanner.Plan(request, []);
            Assert.That(plan.Status, Is.EqualTo(KsProcgenPackingStatus.GeometryReady));
            Assert.That(plan.CellClaims.Count, Is.EqualTo(3));
            var residual = KsProcgenResidualConnector.Connect(shape!, plan,
                [new Vector2i(3, 4), new Vector2i(3, 6)], new HashSet<Vector2i>());
            Assert.That(residual.Status, Is.EqualTo(KsProcgenResidualStatus.PreliminaryReady));
            Assert.That(residual.ReservedPassageCells.Count, Is.EqualTo(3));
            var fill = KsProcgenPureFillPlanner.Plan(shape, plan, request.Seed);
            Assert.That(fill.Status, Is.EqualTo(KsProcgenPureFillStatus.Proposed));
            Assert.That(fill.Zones.Count, Is.EqualTo(1));
            Assert.That(fill.Zones[0].Kind, Is.EqualTo(KsProcgenZoneKind.Passage));
            var partition = KsProcgenPartitionPlanner.Plan(fill, residual.ReservedPassageCells);
            Assert.That(partition.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
            Assert.That(partition.FloorCells.Count, Is.EqualTo(3));
            Assert.That(partition.WallCells, Is.Empty);

            var room = new KsProcgenRoomShape("Entry", [new Vector2i(0, 0), new Vector2i(0, 1)],
                ports: [new KsProcgenRoomPort("North", new Vector2i(0, 1), new Vector2i(0, 1))]);
            var family = new KsProcgenLayoutFamily("TinyPair",
                [new KsProcgenLayoutOption("Entry", room.Cells, [room])]);
            var candidates = KsProcgenLayoutGeometry.FindCandidatesCovering(family, shape,
                new Vector2i(3, 4), 10, out var complete);
            Assert.That(complete, Is.True);
            Assert.That(candidates.Count, Is.EqualTo(1));
            Assert.That(candidates[0].Ports[0].OutsideApproach, Is.EqualTo(new Vector2i(3, 6)));

            var traversal = new KsProcgenTraversalSnapshot();
            foreach (var (cell, _) in plan.CellClaims)
                traversal.WalkableCells.Add(cell);
            traversal.Roots.Add(new Vector2i(3, 4));
            Assert.That(KsProcgenTraversal.Validate(traversal).Valid, Is.True);

            var prototypeManager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themedTiny = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager,
                "KsProcgenSimpleOffice", request.Seed, fill, partition);
            Assert.That(themedTiny.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.Selected),
                themedTiny.Issue?.Message);
            Assert.That(themedTiny.Regions.Count, Is.EqualTo(1));
            Assert.That(themedTiny.Regions[0].Kind, Is.EqualTo(KsProcgenZoneKind.Passage));
            Assert.That(themedTiny.Regions[0].Theme.DominantEntityPackId, Is.Null);
            Assert.That(themedTiny.Regions[0].FloorCells.Count, Is.EqualTo(3));
            var mandatoryTiny = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager,
                "KsProcgenMandatoryOfficeFixture", request.Seed, fill, partition);
            Assert.That(mandatoryTiny.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.ThemeRejected));
            Assert.That(mandatoryTiny.Issue?.Code, Is.EqualTo("MandatoryPackInPassage"));
            Assert.That(mandatoryTiny.Regions, Is.Empty);
            var tinyMaterials = KsProcgenMaterialPlanner.Plan(prototypeManager, themedTiny, partition, request.Seed);
            Assert.That(tinyMaterials.Status, Is.EqualTo(KsProcgenMaterialStatus.Planned));
            Assert.That(tinyMaterials.Tiles.Count, Is.EqualTo(3));
            Assert.That(tinyMaterials.Tiles.All(tile => !tile.Accent), Is.True);
            Assert.That(tinyMaterials.InteriorWalls, Is.Empty);
            Assert.That(tinyMaterials.InteriorDoors, Is.Empty);

            var passageCells = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0) };
            var roomCells = Enumerable.Range(1, 3).SelectMany(y => Enumerable.Range(0, 3)
                .Select(x => new Vector2i(x, y))).ToArray();
            var officeFill = new KsProcgenPureFillResult
            {
                Status = KsProcgenPureFillStatus.Proposed,
                Zones =
                [
                    new KsProcgenZone("passage", KsProcgenZoneKind.Passage, passageCells),
                    new KsProcgenZone("office", KsProcgenZoneKind.RoomProposal, roomCells),
                ],
                ProceduralCells = 12,
            };
            var officePartition = KsProcgenPartitionPlanner.Plan(officeFill, passageCells.ToHashSet());
            Assert.That(officePartition.Status, Is.EqualTo(KsProcgenPartitionStatus.Proposed));
            var themedOffice = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager,
                "KsProcgenSimpleOffice", 17, officeFill, officePartition);
            Assert.That(themedOffice.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.Selected),
                themedOffice.Issue?.Message);
            Assert.That(themedOffice.Regions.Count, Is.EqualTo(2));
            var officeRegion = themedOffice.Regions.Single(region => region.Kind == KsProcgenZoneKind.RoomProposal);
            Assert.That(officeRegion.Theme.DominantEntityPackId, Is.EqualTo("KsProcgenOfficeWorkstations"));
            Assert.That(officeRegion.Theme.SupportingEntityPackIds,
                Is.EquivalentTo(new[] { "KsProcgenOfficeStorage" }));
            Assert.That(officeRegion.WallCells.Count, Is.EqualTo(2));
            Assert.That(themedOffice.Regions.Single(region => region.Kind == KsProcgenZoneKind.Passage)
                .Theme.DominantEntityPackId, Is.Null);
            var officeMaterials = KsProcgenMaterialPlanner.Plan(prototypeManager,
                themedOffice, officePartition, 17);
            var replayMaterials = KsProcgenMaterialPlanner.Plan(prototypeManager,
                themedOffice, officePartition, 17);
            Assert.That(officeMaterials.Status, Is.EqualTo(KsProcgenMaterialStatus.Planned));
            Assert.That(officeMaterials.Tiles.Count, Is.EqualTo(10));
            Assert.That(officeMaterials.Tiles.Count(tile => tile.Accent), Is.EqualTo(1));
            Assert.That(officeMaterials.Tiles.Single(tile => tile.Cell == officePartition.DoorOpenings[0].Threshold)
                .Accent, Is.False);
            Assert.That(officeMaterials.InteriorWalls.Count, Is.EqualTo(2));
            Assert.That(officeMaterials.InteriorWalls.All(wall => wall.EntityId == "WallSolid"), Is.True);
            Assert.That(officeMaterials.InteriorDoors.Single().EntityId, Is.EqualTo("Airlock"));
            Assert.That(officeMaterials.Tiles, Is.EqualTo(replayMaterials.Tiles));
            var noDoorTheme = KsProcgenThemeAssignmentPlanner.Plan(prototypeManager,
                "KsProcgenNoDoorThemeFixture", 17, officeFill, officePartition);
            Assert.That(noDoorTheme.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.Selected));
            var noDoorMaterials = KsProcgenMaterialPlanner.Plan(prototypeManager,
                noDoorTheme, officePartition, 17);
            Assert.That(noDoorMaterials.Status, Is.EqualTo(KsProcgenMaterialStatus.NoCompatibleDoor));
            Assert.That(noDoorMaterials.Issue?.Code, Is.EqualTo("MissingInteriorDoorMaterial"));
            Assert.That(noDoorMaterials.Tiles, Is.Empty);

            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager, "KsProcgenSimpleOffice",
                17, "FurnishedOffice", out var furnishingTheme, out var furnishingIssue),
                Is.True, furnishingIssue?.Message);
            var largeFloor = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5)
                .Select(y => new Vector2i(x, y))).ToArray();
            var largeRegion = new KsProcgenThemedRegion("FurnishedOffice", KsProcgenZoneKind.RoomProposal,
                ["FurnishedOffice"], largeFloor, [], furnishingTheme!, []);
            var openPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = largeFloor,
            };
            var furnished = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, openPartition, 17);
            Assert.That(furnished.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed),
                furnished.Issue?.Message);
            Assert.That(furnished.Entities.Select(entity => entity.EntryId),
                Is.EquivalentTo(new[] { "Desk", "Chair", "Console", "WallLocker" }));
            Assert.That(furnished.Entities.Single(entity => entity.EntryId == "Desk").Movement,
                Is.EqualTo(KsProcgenMovementClass.VaultRequired));
            Assert.That(furnished.Entities.Single(entity => entity.EntryId == "Console").InteractionApproach,
                Is.EqualTo(furnished.Entities.Single(entity => entity.EntryId == "Chair").Cell));
            var furnishedChair = furnished.Entities.Single(entity => entity.EntryId == "Chair");
            var furnishedConsole = furnished.Entities.Single(entity => entity.EntryId == "Console");
            var chairFacing = new[]
            {
                new Vector2i(0, -1), new Vector2i(-1, 0),
                new Vector2i(0, 1), new Vector2i(1, 0),
            };
            Assert.That(chairFacing[furnishedChair.QuarterTurns],
                Is.EqualTo(furnishedConsole.Cell - furnishedChair.Cell));
            var furnishedDesk = furnished.Entities.Single(entity => entity.EntryId == "Desk");
            var storage = furnished.Entities.Single(entity => entity.EntryId == "WallLocker");
            Assert.That(System.Math.Abs(storage.Cell.X - furnishedDesk.Cell.X) +
                        System.Math.Abs(storage.Cell.Y - furnishedDesk.Cell.Y), Is.LessThanOrEqualTo(4));
            Assert.That(furnished.Entities.All(entity => !furnished.ProtectedPassageCells.Contains(entity.Cell)),
                Is.True);
            var furnishedReplay = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, openPartition, 17);
            Assert.That(furnished.Entities, Is.EqualTo(furnishedReplay.Entities));
            var litOffice = KsProcgenLightingPlanner.Plan(prototypeManager,
                largeRegion, furnished, 17);
            var litOfficeReplay = KsProcgenLightingPlanner.Plan(prototypeManager,
                largeRegion, furnished, 17);
            Assert.That(litOffice.Status, Is.EqualTo(KsProcgenLightingPlanStatus.Proposed),
                litOffice.Issue?.Message);
            Assert.That(litOffice.Lights, Is.Not.Empty);
            Assert.That(litOffice.Lights, Is.EqualTo(litOfficeReplay.Lights));
            Assert.That(litOffice.Lights.All(light =>
                !furnished.ProtectedPassageCells.Contains(light.Cell) &&
                furnished.Entities.All(entity => entity.Cell != light.Cell)), Is.True);
            Assert.That(litOffice.EstimatedCoveredCells,
                Is.GreaterThanOrEqualTo((int) System.Math.Ceiling(
                    litOffice.TotalFloorCells * litOffice.RequestedCoverage)));
            Assert.That(litOffice.WorkingCoverageVerified, Is.False);
            var sparseLighting = KsProcgenLightingPlanner.Plan(prototypeManager,
                largeRegion, furnished, 17, maxRoomCells: 4);
            Assert.That(sparseLighting.Status, Is.EqualTo(KsProcgenLightingPlanStatus.Sparse));
            Assert.That(sparseLighting.Lights, Is.Empty);
            var cappedLighting = KsProcgenLightingPlanner.Plan(prototypeManager,
                largeRegion, furnished, 17, maxFixtures: 1);
            Assert.That(cappedLighting.Status, Is.EqualTo(KsProcgenLightingPlanStatus.BudgetExceeded));
            var doorPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = largeFloor,
                DoorOpenings =
                [
                    new KsProcgenPartitionDoor("FurnishedOffice", "passage",
                        new Vector2i(2, 0), new Vector2i(2, 1), new Vector2i(2, -1)),
                ],
            };
            var furnishedWithDoor = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, doorPartition, 17);
            Assert.That(furnishedWithDoor.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed));
            Assert.That(furnishedWithDoor.ProtectedPassageCells,
                Does.Contain(new Vector2i(2, 0)));
            Assert.That(furnishedWithDoor.ProtectedPassageCells,
                Does.Contain(new Vector2i(2, 1)));
            Assert.That(furnishedWithDoor.Entities.All(entity =>
                entity.Cell != new Vector2i(2, 0) && entity.Cell != new Vector2i(2, 1)), Is.True);

            var tinyRoom = new KsProcgenThemedRegion("TinyRoom", KsProcgenZoneKind.RoomProposal,
                ["TinyRoom"], request.Shape.Cells, [], furnishingTheme!,
                [new KsProcgenPackMinimum("KsProcgenOfficeWorkstations", 1)]);
            var tinyOpenPartition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed,
                FloorCells = request.Shape.Cells,
            };
            var mandatoryFurnishing = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                tinyRoom, tinyOpenPartition, 17);
            Assert.That(mandatoryFurnishing.Status, Is.EqualTo(KsProcgenFurnishingStatus.MandatoryUnmet));
            Assert.That(mandatoryFurnishing.Entities, Is.Empty);
            var optionalTiny = tinyRoom with { UnplacedRequiredPacks = [] };
            var sparseFurnishing = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                optionalTiny, tinyOpenPartition, 17);
            Assert.That(sparseFurnishing.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(sparseFurnishing.Entities, Is.Empty);
            Assert.That(sparseFurnishing.Omissions.Select(omission => omission.PackId),
                Is.EquivalentTo(new[] { "KsProcgenOfficeWorkstations", "KsProcgenOfficeStorage" }));
            var budgetedFurnishing = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, openPartition, 17, maxCandidateProbes: 1);
            Assert.That(budgetedFurnishing.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));
            Assert.That(budgetedFurnishing.Entities, Is.Empty);
            var tooLargeOptional = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion, openPartition, 17, maxRoomCells: 4);
            Assert.That(tooLargeOptional.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(tooLargeOptional.Entities, Is.Empty);
            Assert.That(tooLargeOptional.Omissions.Count, Is.EqualTo(2));
            var tooLargeRequired = KsProcgenFurnishingPlanner.Plan(prototypeManager,
                largeRegion with
                {
                    UnplacedRequiredPacks = [new KsProcgenPackMinimum("KsProcgenOfficeWorkstations", 1)],
                }, openPartition, 17, maxRoomCells: 4);
            Assert.That(tooLargeRequired.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));

            var purePipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                request, [], "KsProcgenSimpleOffice");
            Assert.That(purePipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned),
                purePipeline.Issue?.Message);
            Assert.That(purePipeline.Materials?.Tiles.Count, Is.EqualTo(3));
            Assert.That(purePipeline.Materials?.InteriorWalls, Is.Empty);
            Assert.That(purePipeline.Furnishings.Count, Is.EqualTo(1));
            Assert.That(purePipeline.Furnishings[0].Proposal.Entities, Is.Empty);
            Assert.That(purePipeline.Lighting.Count, Is.EqualTo(1));
            Assert.That(purePipeline.Lighting[0].Proposal.Status,
                Is.EqualTo(KsProcgenLightingPlanStatus.Sparse));
            Assert.That(purePipeline.Lighting[0].Proposal.WorkingCoverageVerified, Is.False);
            var pureReplay = KsProcgenGeometryPipeline.Plan(prototypeManager,
                request, [], "KsProcgenSimpleOffice");
            Assert.That(purePipeline.SemanticHash, Is.Not.EqualTo(0UL));
            Assert.That(pureReplay.SemanticHash, Is.EqualTo(purePipeline.SemanticHash));
            var hybridRequest = new KsProcgenRequest
            {
                RequestId = "LoadedHybridTiny",
                Mode = KsProcgenMode.Hybrid,
                Shape = request.Shape,
                RootCells = [new Vector2i(3, 6)],
            };
            var hybridPipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                hybridRequest, [family], "KsProcgenSimpleOffice",
                new KsProcgenPackingBudgets { PreferredPrefabCoveragePercent = 67 });
            Assert.That(hybridPipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned),
                hybridPipeline.Issue?.Message);
            Assert.That(hybridPipeline.Packing?.Placements.Count, Is.EqualTo(1));
            Assert.That(hybridPipeline.Materials?.Tiles.Count, Is.EqualTo(1));
            Assert.That(hybridPipeline.SemanticHash, Is.Not.EqualTo(purePipeline.SemanticHash));
            var unknownThemePipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                request, [], "KsProcgenMissingTheme");
            Assert.That(unknownThemePipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.ThemeRejected));
            Assert.That(unknownThemePipeline.Materials, Is.Null);
            Assert.That(unknownThemePipeline.SemanticHash, Is.EqualTo(0UL));
            var roomOnlyRequest = new KsProcgenRequest
            {
                RequestId = "MandatoryRoomPlan",
                Shape = new KsProcgenShapeSpec
                {
                    AddRectangles =
                    [
                        new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(2, 2) },
                    ],
                },
            };
            var requiredContentPipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                roomOnlyRequest, [], "KsProcgenMandatoryOfficeFixture");
            Assert.That(requiredContentPipeline.Status,
                Is.EqualTo(KsProcgenGeometryPipelineStatus.ContentUnmet),
                requiredContentPipeline.Issue?.Message);
            Assert.That(requiredContentPipeline.HasUnplacedRequiredEntityPacks, Is.True);
            Assert.That(requiredContentPipeline.SemanticHash, Is.EqualTo(0UL));
            Assert.That(requiredContentPipeline.Themes?.Regions.Single().UnplacedRequiredPacks.Single()
                .MinimumCount, Is.EqualTo(1));
            var optionalRoomRequest = new KsProcgenRequest
            {
                RequestId = "OptionalOfficePlan",
                Shape = new KsProcgenShapeSpec
                {
                    AddRectangles =
                    [
                        new KsProcgenTileRect { Min = new Vector2i(0, 0), Max = new Vector2i(3, 3) },
                    ],
                },
            };
            var furnishedPipeline = KsProcgenGeometryPipeline.Plan(prototypeManager,
                optionalRoomRequest, [], "KsProcgenSimpleOffice");
            Assert.That(furnishedPipeline.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.GeometryPlanned),
                furnishedPipeline.Issue?.Message);
            Assert.That(furnishedPipeline.Furnishings.Single().Proposal.Entities
                .Select(entity => entity.EntryId), Does.Contain("Console"));
            Assert.That(furnishedPipeline.Lighting.Single().Proposal.Lights, Is.Not.Empty);
            Assert.That(furnishedPipeline.Lighting.Single().Proposal.WorkingCoverageVerified, Is.False);
            Assert.That(furnishedPipeline.SemanticHash, Is.Not.EqualTo(0UL));

            Assert.That(KsProcgenThemeValidator.TryResolve(prototypeManager, "KsProcgenSimpleOffice",
                out var theme, out issue), Is.True, issue?.Message);
            Assert.That(theme?.TilePacks.Count, Is.EqualTo(1));
            Assert.That(theme?.EntityPacks.Count, Is.EqualTo(2));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager, "KsProcgenSimpleOffice",
                seed: 17, roomId: "OfficeA", out var firstChoice, out issue), Is.True, issue?.Message);
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager, "KsProcgenSimpleOffice",
                seed: 17, roomId: "OfficeA", out var replayChoice, out issue), Is.True, issue?.Message);
            Assert.That(replayChoice?.TilePaletteId, Is.EqualTo(firstChoice?.TilePaletteId));
            Assert.That(replayChoice?.WallFamilyId, Is.EqualTo(firstChoice?.WallFamilyId));
            Assert.That(replayChoice?.DominantEntityPackId, Is.EqualTo(firstChoice?.DominantEntityPackId));
            Assert.That(firstChoice?.DominantEntityPackId, Is.EqualTo("KsProcgenOfficeWorkstations"));
            Assert.That(firstChoice?.SupportingEntityPackIds, Is.EquivalentTo(new[] { "KsProcgenOfficeStorage" }));
            Assert.That(KsProcgenThemeValidator.TryResolve(prototypeManager, "KsProcgenMissingTheme",
                out _, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("UnknownTheme"));
            Assert.That(KsProcgenThemeSelector.TrySelect(prototypeManager, "KsProcgenSimpleOffice",
                seed: 17, roomId: "", out _, out issue), Is.False);
            Assert.That(issue?.Code, Is.EqualTo("InvalidRoomId"));
        });
    }
}
