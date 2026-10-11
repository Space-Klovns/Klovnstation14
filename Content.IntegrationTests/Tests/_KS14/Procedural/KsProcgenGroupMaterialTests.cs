using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenGroupMaterialPlanner))]
public sealed class KsProcgenGroupMaterialTests : GameTest
{
    [Test]
    public async Task CompleteOwnedMaterialsIncludeSeparationAndReplayCanonicalChoices()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager);
            var result = Plan(manager, fixture);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), result.Issue?.Code);
            Assert.That(result.Tiles.Count, Is.EqualTo(24));
            Assert.That(result.Walls.Count, Is.EqualTo(6));
            Assert.That(result.SeparationWalls, Is.EquivalentTo(Enumerable.Range(0, 6).Select(x => new Vector2i(x, 2))));
            Assert.That(result.Tiles.Any(tile => tile.Accent), Is.True);
            Assert.That(result.Tiles.Where(tile => fixture.Partition.ProtectedCells.Contains(tile.Cell)).All(tile => !tile.Accent), Is.True);
            Assert.That(result.Doors, Is.Empty);
            Assert.That(result.UnattachedWallCells, Is.Zero);
            Assert.That(result.EngineMaterialsVerified, Is.False);
            Assert.That(result.GasClosureVerified, Is.False);
            var reversed = Copy(fixture.Partition, regions: fixture.Partition.Regions.Reverse().ToArray());
            var themes = new KsProcgenThemeAssignmentResult { Status = fixture.Themes.Status, Regions = fixture.Themes.Regions.Reverse().ToArray() };
            var replay = KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, reversed, themes,
                maximumExpandedCells: result.ExpandedCells);
            Assert.That(replay.MaterialHash, Is.EqualTo(result.MaterialHash));
            fixture.Request.Shape.Cells.Clear();
            Assert.That(result.Tiles.Count, Is.EqualTo(24));
            Assert.That(result.SeparationWalls.Count, Is.EqualTo(6));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PreservedFloorRetainsItsMaterialWithoutAnyWriteChoice(bool constant)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, preservedEntry: true, constant: constant);
            var result = Plan(manager, fixture);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), result.Issue?.Code);
            Assert.That(result.Tiles.Count, Is.EqualTo(23));
            Assert.That(result.Tiles.Select(tile => tile.Cell), Does.Not.Contain(new Vector2i(0, 0)));
            Assert.That(result.Walls.Select(wall => wall.Cell), Does.Not.Contain(new Vector2i(0, 0)));
            Assert.That(result.Doors.Select(door => door.Cell), Does.Not.Contain(new Vector2i(0, 0)));
        });
    }

    [Test]
    public async Task ProtectedRoutesRemainPrimaryWhenEveryTileIsRequestedAsAccent()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager);
            var themes = new KsProcgenThemeAssignmentResult
            {
                Status = fixture.Themes.Status, Regions = fixture.Themes.Regions.Select(region => region with
                    { Theme = region.Theme with { TilePackId = "KsProcgenAccentTilesFixture", TilePaletteId = "Accent" } }).ToArray(),
            };
            var result = KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, fixture.Partition, themes);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), result.Issue?.Code);
            Assert.That(result.Tiles.Count(tile => !tile.Accent), Is.EqualTo(fixture.Partition.ProtectedCells.Count));
            Assert.That(result.AccentShortfall, Is.EqualTo(fixture.Partition.ProtectedCells.Count));
            Assert.That(result.Tiles.Where(tile => fixture.Partition.ProtectedCells.Contains(tile.Cell)).All(tile => !tile.Accent), Is.True);
        });
    }

    [Test]
    public async Task PrototypeReferencesAreRecheckedBeforeAcceptingUnwalledRoomMaterials()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager);
            var packId = "KsProcgenReinforcedWallsFixture";
            var family = manager.Index<KsProcgenWallPackPrototype>(packId).Families.Single();
            var original = family.InteriorWall;
            try
            {
                family.InteriorWall = "KsProcgenMissingMaterialEntity";
                var themes = new KsProcgenThemeAssignmentResult
                {
                    Status = fixture.Themes.Status, Regions = fixture.Themes.Regions.Select(region => region with
                        { Theme = region.Theme with { WallPackId = packId, WallFamilyId = "Reinforced" } }).ToArray(),
                };
                AssertFailure(KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, fixture.Partition, themes),
                    "MissingOwnedMaterialPrototype", KsProcgenGroupMaterialStatus.InvalidInput);
            }
            finally
            {
                family.InteriorWall = original;
            }
        });
    }

    [Test]
    public async Task InternalDoorChoicesRemainPrimaryAndCannotCrossOwners()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, splitRooms: true);
            var result = Plan(manager, fixture);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), result.Issue?.Code);
            Assert.That(result.Doors.Count, Is.GreaterThan(0));
            Assert.That(result.Doors.Count, Is.EqualTo(fixture.Partition.Regions.Sum(network => network.Partition.DoorOpenings.Count)));
            Assert.That(result.DoorOpenings.Select(opening => opening.Threshold), Is.EqualTo(result.Doors.Select(door => door.Cell)));
            Assert.That(result.Doors.All(door => !result.Tiles.Single(tile => tile.Cell == door.Cell).Accent), Is.True);
            var first = fixture.Partition.Regions[0];
            var door = first.Partition.DoorOpenings[0];
            var changed = CopyPartition(first.Partition, doors: new[] { door with { OutsideApproach = new(0, 4) } });
            var networks = fixture.Partition.Regions.ToArray();
            networks[0] = first with { Partition = changed };
            AssertFailure(KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, Copy(fixture.Partition, regions: networks), fixture.Themes),
                "InvalidOwnedMaterialDoor", KsProcgenGroupMaterialStatus.InvalidInput);
        });
    }

    [Test]
    public async Task MissingLaterDoorMaterialDiscardsAllEarlierChoices()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, splitRooms: true);
            var regions = fixture.Themes.Regions.Select(region => region.EntranceGroupIds.Contains("B")
                ? region with { Theme = region.Theme with { WallPackId = "KsProcgenNoDoorWallsFixture", WallFamilyId = "NoDoor" } } : region).ToArray();
            var themes = new KsProcgenThemeAssignmentResult { Status = fixture.Themes.Status, Regions = regions };
            AssertFailure(KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, fixture.Partition, themes),
                "MissingInteriorDoorMaterial", KsProcgenGroupMaterialStatus.IncompatibleMaterials);
        });
    }

    [Test]
    public async Task DifferentIncidentWallFamiliesRejectTheCompleteSeparator()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager);
            var regions = fixture.Themes.Regions.Select(region => region.EntranceGroupIds.Contains("B")
                ? region with { Theme = region.Theme with { WallPackId = "KsProcgenReinforcedWallsFixture", WallFamilyId = "Reinforced" } } : region).ToArray();
            AssertFailure(KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, fixture.Partition,
                new() { Status = fixture.Themes.Status, Regions = regions }),
                "ConflictingSeparationWallMaterials", KsProcgenGroupMaterialStatus.IncompatibleMaterials);
        });
    }

    [Test]
    public async Task SourceAndSeparatorFloodsShareOneExactAllowance()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager);
            var full = Plan(manager, fixture);
            var limited = KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes,
                maximumExpandedCells: full.ExpandedCells - 1);
            AssertFailure(limited, "GroupMaterialExpansionBudget", KsProcgenGroupMaterialStatus.BudgetExceeded);
            Assert.That(limited.ExpandedCells, Is.EqualTo(full.ExpandedCells - 1));
            var zero = KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes,
                maximumExpandedCells: 0);
            AssertFailure(zero, "EntranceSceneExpansionBudget", KsProcgenGroupMaterialStatus.BudgetExceeded);
        });
    }

    [Test]
    public async Task MissingSeparatorDispositionAndStaleHostSceneAreRejected()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager);
            AssertFailure(KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request,
                Copy(fixture.Partition, walls: fixture.Partition.WallCells.Skip(1).ToArray()), fixture.Themes),
                "StaleGroupMaterialSource", KsProcgenGroupMaterialStatus.InvalidInput);
            var stale = Copy(fixture.Partition, scene: fixture.Partition.Scene! with
                { CleanHostCells = fixture.Partition.Scene.CleanHostCells.Append(new Vector2i(-3, 0)).ToHashSet() });
            AssertFailure(KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, stale, fixture.Themes),
                "StaleGroupMaterialSource", KsProcgenGroupMaterialStatus.InvalidInput);
        });
    }

    [Test]
    public async Task ForeignThemeLabelsAndMissingReservationsAreRejected()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager);
            foreach (var changed in new[]
            {
                fixture.Themes.Regions[0] with { EntranceGroupIds = new[] { "B" } },
                fixture.Themes.Regions[0] with { ProtectedPassageCells = new Vector2i[0] },
            })
            {
                var regions = fixture.Themes.Regions.ToArray();
                regions[0] = changed;
                AssertFailure(KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, fixture.Partition,
                    new() { Status = fixture.Themes.Status, Regions = regions }),
                    "InvalidMaterialOwnership", KsProcgenGroupMaterialStatus.InvalidInput);
            }
        });
    }

    [Test]
    public async Task OneByThreePassageAndUnreachableWallIslandKeepCompleteDisposition()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, tiny: true);
            var result = Plan(manager, fixture);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), result.Issue?.Code);
            Assert.That(result.Tiles.Count, Is.EqualTo(3));
            Assert.That(result.Tiles.All(tile => !tile.Accent), Is.True);
            Assert.That(result.Walls.Single().Cell, Is.EqualTo(new Vector2i(9, 9)));
            Assert.That(result.UnattachedWallCells, Is.EqualTo(1));
            Assert.That(result.Doors, Is.Empty);
        });
    }

    [Test]
    public async Task HullWallsCoverOnlyClaimableEnvelopeAndReplayWithoutSealingEntrances()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, envelope: true);
            var result = KsProcgenGroupHullWallPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), result.Issue?.Code);
            Assert.That(result.HullWalls.Count, Is.EqualTo(18));
            Assert.That(result.HullWalls.Select(wall => wall.Cell), Is.EquivalentTo(result.Boundary!.RequiredEnvelopeCells));
            Assert.That(result.HullWalls.All(wall => wall.EntityId == "WallReinforced"), Is.True);
            Assert.That(result.HullWalls.All(wall => !result.Boundary.ReservedEnvelopeCells.Contains(wall.Cell)), Is.True);
            Assert.That(result.Boundary.Edges.Count(edge => edge.Kind == KsProcgenOwnedBoundaryKind.OperationalEntrance), Is.EqualTo(2));
            Assert.That(result.HasUnresolvedBoundary, Is.True);
            Assert.That(result.GasClosureVerified, Is.False);
            Assert.That(result.WindowGoalVerified, Is.False);
            Assert.That(result.EngineMaterialsVerified, Is.False);
            var replay = KsProcgenGroupHullWallPlanner.Plan(manager, fixture.Request, fixture.Partition,
                new() { Status = fixture.Themes.Status, Regions = fixture.Themes.Regions.Reverse().ToArray() },
                maximumExpandedCells: result.ExpandedCells);
            Assert.That(replay.WallProposalHash, Is.EqualTo(result.WallProposalHash));
            fixture.Request.Shape.EnvelopeCells.Clear();
            Assert.That(result.HullWalls.Count, Is.EqualTo(18));
        });
    }

    [Test]
    public async Task HullRevalidationAndSelectionShareTheirBudgetAndMissingFamiliesFailAtomically()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, envelope: true);
            var full = KsProcgenGroupHullWallPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes);
            var limited = KsProcgenGroupHullWallPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes,
                maximumExpandedCells: full.ExpandedCells - 1);
            AssertHullFailure(limited, "HullWallSelectionExpansionBudget", KsProcgenGroupMaterialStatus.BudgetExceeded);
            Assert.That(limited.ExpandedCells, Is.EqualTo(full.ExpandedCells - 1));
            var regions = fixture.Themes.Regions.Select(region => region.EntranceGroupIds.Contains("B")
                ? region with { Theme = region.Theme with { WallPackId = "KsProcgenNoDoorWallsFixture", WallFamilyId = "NoDoor" } } : region).ToArray();
            AssertHullFailure(KsProcgenGroupHullWallPlanner.Plan(manager, fixture.Request, fixture.Partition,
                new() { Status = fixture.Themes.Status, Regions = regions }),
                "MissingOwnedHullWallMaterial", KsProcgenGroupMaterialStatus.IncompatibleMaterials);
        });
    }

    [Test]
    public async Task AnEnvelopeCellSharedBySeparateNetworksRequiresCompatibleHullFamilies()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, envelope: true, sharedHole: true);
            var initial = KsProcgenGroupHullWallPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes);
            Assert.That(initial.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), initial.Issue?.Code);
            Assert.That(initial.HullWalls.Select(wall => wall.Cell), Does.Contain(new Vector2i(1, 1)));
            Assert.That(initial.Boundary!.Edges.Where(edge => edge.BoundaryCell == new Vector2i(1, 1))
                .Select(edge => edge.Component).Distinct().Count(), Is.EqualTo(2));
            var regions = fixture.Themes.Regions.Select(region => region.EntranceGroupIds.Contains("B")
                ? region with { Theme = region.Theme with { WallPackId = "KsProcgenAlternateHullFixture", WallFamilyId = "Alternate" } } : region).ToArray();
            AssertHullFailure(KsProcgenGroupHullWallPlanner.Plan(manager, fixture.Request, fixture.Partition,
                new() { Status = fixture.Themes.Status, Regions = regions }),
                "ConflictingOwnedHullWallMaterials", KsProcgenGroupMaterialStatus.IncompatibleMaterials);
        });
    }

    [Test]
    public async Task MissingEnvelopeProducesUnknownBoundariesWithoutInventingWallCells()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager);
            var result = KsProcgenGroupHullWallPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.Planned), result.Issue?.Code);
            Assert.That(result.HullWalls, Is.Empty);
            Assert.That(result.HasUnresolvedBoundary, Is.True);
            Assert.That(result.Boundary!.Edges.Any(edge => edge.Kind == KsProcgenOwnedBoundaryKind.MissingFootprintHull), Is.True);
            Assert.That(result.GasClosureVerified, Is.False);
        });
    }

    [Test]
    public async Task ExteriorWindowsReplaceWallsExactlyOnceAndNeverReplaceEntranceSpans()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, envelope: true);
            fixture.Request.WindowGoal = new() { ExteriorWindowFraction = 0.5f, HardFraction = true, ToleranceCells = 0 };
            var facts = OuterFaces(fixture);
            var result = Windows(manager, fixture, facts);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupWindowStatus.Planned), result.Issue?.Code);
            Assert.That(result.Selection!.EligibleCells, Is.EqualTo(18));
            Assert.That(result.Windows.Count, Is.EqualTo(9));
            Assert.That(result.Windows.All(window => window.EntityId == "Window"), Is.True);
            var original = result.SourceHull!.InteriorMaterials!.Walls.Concat(result.SourceHull.HullWalls).Select(wall => wall.Cell).ToArray();
            Assert.That(result.RemainingWalls.Select(wall => wall.Cell).Concat(result.Windows.Select(window => window.Cell)),
                Is.EquivalentTo(original));
            Assert.That(result.RemainingWalls.Select(wall => wall.Cell).Intersect(result.Windows.Select(window => window.Cell)), Is.Empty);
            Assert.That(result.Windows.All(window => !result.SourceHull.Boundary!.ReservedEnvelopeCells.Contains(window.Cell)), Is.True);
            Assert.That(result.WindowGoalVerified || result.GasClosureVerified || result.EngineMaterialsVerified, Is.False);
            Assert.That(result.Boundary!.ExteriorScopeComplete, Is.False, "Separating target walls still need non-exterior face facts.");
            var replay = KsProcgenGroupWindowPlanner.Plan(manager, fixture.Request, fixture.Partition,
                new() { Status = fixture.Themes.Status, Regions = fixture.Themes.Regions.Reverse().ToArray() },
                facts.AsEnumerable().Reverse().ToArray(), maximumExpandedCells: result.ExpandedCells);
            Assert.That(replay.WindowProposalHash, Is.EqualTo(result.WindowProposalHash));
            facts.Clear(); fixture.Request.Shape.EnvelopeCells.Clear();
            Assert.That(result.Windows.Count, Is.EqualTo(9));
            Assert.That(result.Selection.ChosenWindowCells, Is.Not.InstanceOf<Vector2i[]>());
            Assert.That(result.Selection.ExcludedCells, Is.Not.InstanceOf<KsProcgenWindowExclusion[]>());
        });
    }

    [Test]
    public async Task MissingWindowFamilyExcludesItsCellsAndHardCountsDiscardAllEarlierProposals()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, envelope: true);
            var regions = fixture.Themes.Regions.Select(region => region.EntranceGroupIds.Contains("B")
                ? region with { Theme = region.Theme with { WallPackId = "KsProcgenNoWindowsFixture", WallFamilyId = "NoWindows" } } : region).ToArray();
            var themes = new KsProcgenThemeAssignmentResult { Status = fixture.Themes.Status, Regions = regions };
            var facts = OuterFaces(fixture);
            var result = KsProcgenGroupWindowPlanner.Plan(manager, fixture.Request, fixture.Partition, themes, facts);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupWindowStatus.Planned), result.Issue?.Code);
            Assert.That(result.Selection!.EligibleCells, Is.EqualTo(9));
            Assert.That(result.Selection.ExcludedCells.Count(cell => cell.Reason == KsProcgenWindowExclusionReason.UnsupportedAirtightWindow),
                Is.EqualTo(9));
            Assert.That(result.Windows.All(window => fixture.Themes.Regions.Single(region => region.Id == window.RegionId)
                .EntranceGroupIds.Contains("A")), Is.True);
            fixture.Request.WindowGoal.MinimumCount = 10;
            AssertWindowFailure(KsProcgenGroupWindowPlanner.Plan(manager, fixture.Request, fixture.Partition, themes, facts),
                "WindowCountInfeasible", KsProcgenGroupWindowStatus.HardTargetUnmet);
        });
    }

    [Test]
    public async Task NoExteriorContextDoesNotInventEligibilityOrSatisfyAHardMinimum()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, envelope: true);
            var result = Windows(manager, fixture, []);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupWindowStatus.Planned), result.Issue?.Code);
            Assert.That(result.Selection!.Status, Is.EqualTo(KsProcgenWindowPlanStatus.NotApplicable));
            Assert.That(result.Windows, Is.Empty);
            Assert.That(result.Boundary!.UnclassifiedStructuralCells.Count, Is.GreaterThan(0));
            fixture.Request.WindowGoal.MinimumCount = 1;
            AssertWindowFailure(Windows(manager, fixture, []), "WindowCountInfeasible", KsProcgenGroupWindowStatus.HardTargetUnmet);
        });
    }

    [Test]
    public async Task StructuralAuthorExclusionsAffectCountsAndReplayIdentity()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, envelope: true);
            fixture.Request.WindowGoal.ExteriorWindowFraction = 1f;
            var facts = OuterFaces(fixture);
            var original = Windows(manager, fixture, facts);
            facts[0] = facts[0] with { RequiredStructure = true };
            facts[1] = facts[1] with { WindowsDisabled = true };
            facts[2] = facts[2] with { SupportsAirtightWindow = false };
            var result = Windows(manager, fixture, facts);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupWindowStatus.Planned), result.Issue?.Code);
            Assert.That(result.Windows.Count, Is.EqualTo(15));
            Assert.That(result.Selection!.ExcludedCells.Select(cell => cell.Reason), Is.SupersetOf(new[]
            {
                KsProcgenWindowExclusionReason.RequiredStructure, KsProcgenWindowExclusionReason.DisabledByAuthor,
                KsProcgenWindowExclusionReason.UnsupportedAirtightWindow,
            }));
            Assert.That(result.Windows.Select(window => window.Cell).Intersect(facts.Take(3).Select(fact => fact.Cell)), Is.Empty);
            Assert.That(result.WindowProposalHash, Is.Not.EqualTo(original.WindowProposalHash));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FixedExteriorWindowsCountWithoutBeingEmittedOrOverwritten(bool constant)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, envelope: true, fixedExterior: true, constantExterior: constant);
            var facts = OuterFaces(fixture);
            var fixedCell = new Vector2i(2, 5);
            facts.Add(new(fixedCell, new(0, 1), true, true, IsExistingWindow: true));
            fixture.Request.WindowGoal = new() { ExteriorWindowFraction = 0.5f, HardFraction = true, ToleranceCells = 0 };
            var result = Windows(manager, fixture, facts);
            Assert.That(result.Status, Is.EqualTo(KsProcgenGroupWindowStatus.Planned), result.Issue?.Code);
            Assert.That(result.Selection!.EligibleCells, Is.EqualTo(18));
            Assert.That(result.Selection.FixedWindowCells, Is.EqualTo(1));
            Assert.That(result.Selection.AchievedWindowCells, Is.EqualTo(9));
            Assert.That(result.Windows.Count, Is.EqualTo(8));
            Assert.That(result.Windows.Select(window => window.Cell), Does.Not.Contain(fixedCell));
            Assert.That(result.RemainingWalls.Select(wall => wall.Cell), Does.Not.Contain(fixedCell));
            fixture.Request.WindowGoal.ExteriorWindowFraction = 0f;
            AssertWindowFailure(Windows(manager, fixture, facts), "WindowFractionInfeasible", KsProcgenGroupWindowStatus.HardTargetUnmet);
            fixture.Request.WindowGoal.HardFraction = false;
            var soft = Windows(manager, fixture, facts);
            Assert.That(soft.Status, Is.EqualTo(KsProcgenGroupWindowStatus.Planned));
            Assert.That(soft.Selection!.AchievedWindowCells, Is.EqualTo(1));
            Assert.That(soft.Windows, Is.Empty);
        });
    }

    [Test]
    public async Task WindowStagesShareWorkAndRejectStaleOrInvalidContextAtomically()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(manager, envelope: true);
            var facts = OuterFaces(fixture);
            var full = Windows(manager, fixture, facts);
            Assert.That(full.Status, Is.EqualTo(KsProcgenGroupWindowStatus.Planned));
            var limited = KsProcgenGroupWindowPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes, facts,
                maximumExpandedCells: full.ExpandedCells - 1);
            AssertWindowFailure(limited, "OwnedWindowPrototypeBudget", KsProcgenGroupWindowStatus.BudgetExceeded);
            Assert.That(limited.ExpandedCells, Is.EqualTo(full.ExpandedCells - 1));
            facts.Add(facts[0]);
            AssertWindowFailure(Windows(manager, fixture, facts), "InvalidOwnedWindowFacts", KsProcgenGroupWindowStatus.InvalidInput);
            facts.RemoveAt(facts.Count - 1);
            fixture.Request.Shape.EnvelopeCells.Add(new(20, 20));
            AssertWindowFailure(Windows(manager, fixture, facts), "StaleGroupMaterialSource", KsProcgenGroupWindowStatus.InvalidInput);
        });
    }

    private static List<KsProcgenExteriorWindowFact> OuterFaces(MaterialFixture fixture)
    {
        var hull = KsProcgenGroupHullBoundaryAnalyzer.Analyze(fixture.Request, fixture.Partition);
        Assert.That(hull.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), hull.Issue?.Code);
        var target = fixture.Request.Shape.Cells.ToHashSet();
        var envelope = fixture.Request.Shape.EnvelopeCells.ToHashSet();
        return hull.Edges.Where(edge => edge.Kind == KsProcgenOwnedBoundaryKind.WritableEnvelope)
            .GroupBy(edge => edge.BoundaryCell).Where(group => group.Count() == 1)
            .Select(group => new KsProcgenExteriorWindowFact(group.Key, group.Key - group.Single().FloorCell, true, true))
            .Where(fact => !target.Contains(fact.Cell + fact.OutwardNormal) && !envelope.Contains(fact.Cell + fact.OutwardNormal))
            .OrderBy(fact => fact.Cell.Y).ThenBy(fact => fact.Cell.X).ToList();
    }
    private static KsProcgenGroupWindowResult Windows(IPrototypeManager manager, MaterialFixture fixture,
        IReadOnlyList<KsProcgenExteriorWindowFact> facts) =>
        KsProcgenGroupWindowPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes, facts);
    private static void AssertWindowFailure(KsProcgenGroupWindowResult result, string code, KsProcgenGroupWindowStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Issue!.Code, Is.EqualTo(code));
        Assert.That(result.Windows, Is.Empty); Assert.That(result.RemainingWalls, Is.Empty);
        Assert.That(result.SourceHull, Is.Null); Assert.That(result.Boundary, Is.Null); Assert.That(result.Selection, Is.Null);
        Assert.That(result.WindowProposalHash, Is.Zero);
    }

    private static void AssertHullFailure(KsProcgenGroupHullWallResult result, string code, KsProcgenGroupMaterialStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Issue!.Code, Is.EqualTo(code));
        Assert.That(result.HullWalls, Is.Empty);
        Assert.That(result.Boundary, Is.Null);
        Assert.That(result.InteriorMaterials, Is.Null);
        Assert.That(result.WallProposalHash, Is.Zero);
        Assert.That(result.HasUnresolvedBoundary, Is.True);
    }

    private sealed record MaterialFixture(KsProcgenRequest Request, KsProcgenGroupPartitionResult Partition,
        KsProcgenThemeAssignmentResult Themes);

    private static MaterialFixture Fixture(IPrototypeManager manager, bool preservedEntry = false, bool splitRooms = false,
        bool tiny = false, bool constant = false, bool envelope = false, bool sharedHole = false,
        bool fixedExterior = false, bool constantExterior = false)
    {
        var request = new KsProcgenRequest
        {
            RequestId = "MaterialAudit", Seed = 23, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { Cells = tiny ? new List<Vector2i> { new(0, 0), new(1, 0), new(2, 0), new(9, 9) }
                : Enumerable.Range(0, sharedHole ? 3 : 6).SelectMany(x => Enumerable.Range(0, sharedHole ? 3 : 5)
                    .Select(y => new Vector2i(x, y))).Where(cell => !sharedHole || cell != new Vector2i(1, 1)).ToList() },
            EntranceDomain = new()
            {
                Entrances = new List<KsProcgenEntranceSpec>
                    { new() { PortId = "A1", ThresholdCells = new List<Vector2i> { new(-1, 0) }, InwardNormal = new(1, 0) } },
                Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Separate",
                    Groups = new List<KsProcgenEntranceGroupSpec> { new() { Id = "A", Ports = new List<string> { "A1" } } } } },
            },
        };
        if (envelope)
        {
            var target = request.Shape.Cells.ToHashSet();
            request.Shape.EnvelopeCells = target.SelectMany(cell => new[] { cell + new Vector2i(1, 0), cell + new Vector2i(-1, 0),
                cell + new Vector2i(0, 1), cell + new Vector2i(0, -1) }).Where(cell => !target.Contains(cell)).Distinct().ToList();
        }
        var host = new HashSet<Vector2i> { new(-1, 0), new(-2, 0) };
        if (!tiny)
        {
            var secondY = sharedHole ? 2 : 4;
            request.EntranceDomain.Entrances.Add(new() { PortId = "B1", ThresholdCells = new List<Vector2i> { new(-1, secondY) }, InwardNormal = new(1, 0) });
            request.EntranceDomain.Configurations[0].Groups.Add(new() { Id = "B", Ports = new List<string> { "B1" } });
            host.UnionWith(new[] { new Vector2i(-1, secondY), new Vector2i(-2, secondY) });
        }
        if (preservedEntry)
        {
            if (constant)
                request.ConstantRegions.Add(new() { Id = "Entry", SourceId = "MaterialFixture", ContentFingerprint = "Declared",
                    LocalCells = new List<Vector2i> { new(0, 0) } });
            else
                request.Shape.PreservedCells = new List<Vector2i> { new(0, 0) };
        }
        if (fixedExterior)
        {
            request.Shape.Cells.Add(new(2, 5)); request.Shape.EnvelopeCells.Remove(new(2, 5));
            if (constantExterior)
                request.ConstantRegions.Add(new() { Id = "FixedWindow", SourceId = "MaterialFixture", ContentFingerprint = "Declared",
                    LocalCells = new List<Vector2i> { new(2, 5) } });
            else request.Shape.PreservedCells.Add(new(2, 5));
        }
        var context = new KsProcgenGroupRouteContext(preservedEntry ? new HashSet<Vector2i> { new(0, 0) } : new HashSet<Vector2i>(),
            host, new HashSet<Vector2i>(), true, true);
        var floor = KsProcgenGroupFloorPlanner.Plan(request, "Separate", context);
        Assert.That(floor.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), floor.Issue?.Code);
        var zones = splitRooms ? floor.Regions.SelectMany(network => new[]
        {
            new KsProcgenZone(network.Id + "/Left", KsProcgenZoneKind.RoomProposal, network.FloorCells.Where(cell => cell.X < 3).ToArray()),
            new KsProcgenZone(network.Id + "/Right", KsProcgenZoneKind.RoomProposal, network.FloorCells.Where(cell => cell.X >= 3).ToArray()),
        }).ToArray() : null;
        var partition = KsProcgenGroupPartitionPlanner.Plan(request, floor, preferredMaxRoomCells: 64, zones: zones);
        Assert.That(partition.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate), partition.Issue?.Code);
        var themes = KsProcgenGroupThemePlanner.Plan(manager, "KsProcgenSimpleOffice", request.Seed, partition);
        Assert.That(themes.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.Selected), themes.Issue?.Code);
        return new(request, partition, themes);
    }

    private static KsProcgenGroupMaterialResult Plan(IPrototypeManager manager, MaterialFixture fixture) =>
        KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, fixture.Partition, fixture.Themes);
    private static KsProcgenGroupPartitionResult Copy(KsProcgenGroupPartitionResult source,
        IReadOnlyList<KsProcgenOwnedPartition>? regions = null, IReadOnlyList<Vector2i>? walls = null, KsProcgenEntranceScene? scene = null) => new()
    {
        Status = source.Status, Regions = regions ?? source.Regions, WallCells = walls ?? source.WallCells,
        FloorCells = source.FloorCells, ProtectedCells = source.ProtectedCells, Scene = scene ?? source.Scene,
        Analysis = source.Analysis, PartitionHash = source.PartitionHash,
    };
    private static KsProcgenPartitionResult CopyPartition(KsProcgenPartitionResult source,
        IReadOnlyList<KsProcgenPartitionDoor> doors) => new()
    {
        Status = source.Status, FloorCells = source.FloorCells, WallCells = source.WallCells,
        DoorOpenings = doors, MergedZoneGroups = source.MergedZoneGroups,
    };
    private static void AssertFailure(KsProcgenGroupMaterialResult result, string code, KsProcgenGroupMaterialStatus status)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        Assert.That(result.Issue!.Code, Is.EqualTo(code));
        Assert.That(result.Tiles, Is.Empty);
        Assert.That(result.Walls, Is.Empty);
        Assert.That(result.Doors, Is.Empty);
        Assert.That(result.DoorOpenings, Is.Empty);
        Assert.That(result.SeparationWalls, Is.Empty);
        Assert.That(result.MaterialHash, Is.Zero);
    }
}
