using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenSelectedContent))]
public sealed class KsProcgenSelectedContentTests : KsProcgenGameTest
{
    [Test]
    public async Task EarlierMaterialsFurnitureAndLightingIgnoreLaterPrototypeEdits()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenSimpleOffice";
            Assert.That(KsProcgenThemeSelector.TrySelect(manager, themeId, 17, "Office", out var selected, out var issue), Is.True, issue?.Code);
            var selection = selected!;
            var floor = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToArray();
            var partition = new KsProcgenPartitionResult { Status = KsProcgenPartitionStatus.Proposed, FloorCells = floor };
            var region = new KsProcgenThemedRegion("Office", KsProcgenZoneKind.RoomProposal, ["Office"], floor, [], selection, []);
            var assignment = new KsProcgenThemeAssignmentResult { Status = KsProcgenThemeAssignmentStatus.Selected, Regions = [region] };
            var materials = KsProcgenMaterialPlanner.Plan(manager, assignment, partition, 17);
            var furnishings = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17);
            var lights = KsProcgenLightingPlanner.Plan(manager, region, furnishings, 17);
            Assert.That(materials.Status, Is.EqualTo(KsProcgenMaterialStatus.Planned));
            Assert.That(furnishings.Entities, Is.Not.Empty);
            Assert.That(lights.Status, Is.AnyOf(KsProcgenLightingPlanStatus.Proposed, KsProcgenLightingPlanStatus.Sparse));
            var palette = manager.Index<KsProcgenTilePackPrototype>(selection.TilePackId).Palettes.Single(item => item.Id == selection.TilePaletteId);
            var family = manager.Index<KsProcgenWallPackPrototype>(selection.WallPackId).Families.Single(item => item.Id == selection.WallFamilyId);
            var fixture = manager.Index<KsProcgenLightingPackPrototype>(selection.LightingPackId!).Fixtures.Single(item => item.Id == selection.LightFixtureId);
            var theme = manager.Index<KsProcgenRoomThemePrototype>(themeId);
            var pack = manager.Index<KsProcgenEntityPackPrototype>(selection.DominantEntityPackId!);
            var assembly = manager.Index<KsProcgenAssemblyPrototype>(pack.Assemblies.Single().Assembly);
            var member = assembly.Members[0];
            var savedTile = palette.PrimaryTile;
            var savedWall = family.InteriorWall;
            var savedSpacing = fixture.PreferredSpacing;
            var savedCoverage = theme.Goals!.LightingCoverage;
            var savedDensity = theme.Goals.FurnishingDensity;
            var savedEntity = member.Entity;
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                palette.PrimaryTile = savedTile == "FloorSteel" ? "FloorSteelMini" : "FloorSteel";
                family.InteriorWall = "WallReinforced";
                fixture.PreferredSpacing += 4;
                theme.Goals.LightingCoverage = 0.1f;
                theme.Goals.FurnishingDensity = 0.9f;
                member.Entity = "ClosetWallGrey";
                var retainedMaterials = KsProcgenMaterialPlanner.Plan(manager, assignment, partition, 17);
                var retainedFurniture = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17);
                var retainedLights = KsProcgenLightingPlanner.Plan(manager, region, retainedFurniture, 17);
                Assert.That(retainedMaterials.Tiles, Is.EqualTo(materials.Tiles));
                Assert.That(retainedFurniture.Entities.Select(entity => (entity.EntityId, entity.Cell, entity.QuarterTurns)),
                    Is.EqualTo(furnishings.Entities.Select(entity => (entity.EntityId, entity.Cell, entity.QuarterTurns))));
                Assert.That(retainedLights.RequestedCoverage, Is.EqualTo(savedCoverage));
                Assert.That(retainedLights.Lights, Is.EqualTo(lights.Lights));
                Assert.That(KsProcgenSelectedContent.TryWall(manager, selection, out var wall), Is.True);
                Assert.That(wall!.InteriorWall, Is.EqualTo(savedWall));
                Assert.That(KsProcgenThemeSelector.TrySelect(manager, themeId, 17, "Office", out var fresh, out issue), Is.True, issue?.Code);
                Assert.That(fresh!.Content!.Tile.PrimaryTile, Is.EqualTo(palette.PrimaryTile));
                Assert.That(fresh.Content.Goals.LightingCoverage, Is.EqualTo(0.1f));
                Assert.That(fresh.Content.EntityPacks.Single(item => item.Id == pack.ID).Cores.Single().Variants.First().Members
                    .Single(item => item.SourceMemberId == member.Id).Entry.Entity, Is.EqualTo("ClosetWallGrey"));
                Assert.Throws<NotSupportedException>(() => ((IList<string>) selection.SupportingEntityPackIds).Clear());
                Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenSelectedEntityPack>) selection.Content!.EntityPacks).Clear());
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                palette.PrimaryTile = savedTile; family.InteriorWall = savedWall; fixture.PreferredSpacing = savedSpacing;
                theme.Goals.LightingCoverage = savedCoverage; theme.Goals.FurnishingDensity = savedDensity; member.Entity = savedEntity;
            }
        });
    }

    [Test]
    public async Task MismatchedSelectionIdsCannotReuseCapturedBodiesAndLegacyReferencesStillResolve()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenSimpleOffice";
            Assert.That(KsProcgenThemeSelector.TrySelect(manager, themeId, 17, "Office", out var selection, out var issue), Is.True, issue?.Code);
            Assert.That(KsProcgenSelectedContent.TryTile(manager, selection! with { TilePaletteId = "Other" }, out var tile), Is.False);
            Assert.That(tile, Is.Null);
            Assert.That(KsProcgenSelectedContent.TryWall(manager, selection! with { WallPackId = "Other" }, out var wall), Is.False);
            Assert.That(wall, Is.Null);
            Assert.That(KsProcgenSelectedContent.TryLight(manager, selection! with { LightFixtureId = "Other" }, out var light, out _), Is.False);
            Assert.That(light, Is.Null);
            Assert.That(KsProcgenSelectedContent.TryTile(manager, selection! with { Content = null }, out tile), Is.True);
            Assert.That(tile, Is.EqualTo(selection!.Content!.Tile));
            Assert.That(KsProcgenSelectedContent.TryWall(manager, selection with { Content = null }, out wall), Is.True);
            Assert.That(wall, Is.EqualTo(selection.Content.Wall));
            var floor = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0) };
            var region = new KsProcgenThemedRegion("ChangedPack", KsProcgenZoneKind.RoomProposal, ["ChangedPack"], floor, [],
                selection with { DominantEntityPackId = "Uncaptured" }, []);
            var partition = new KsProcgenPartitionResult { Status = KsProcgenPartitionStatus.Proposed, FloorCells = floor };
            var rejected = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17);
            Assert.That(rejected.Issue?.Code, Is.EqualTo("UncapturedFurnishingPack"));
            Assert.That(rejected.Entities, Is.Empty);
            Assert.That(rejected.CandidateProbes, Is.Zero);
        });
    }
}
