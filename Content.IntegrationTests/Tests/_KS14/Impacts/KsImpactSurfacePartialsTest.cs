using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Impacts;
using Content.Shared._KS14.Particles;
using System.Linq;
using Content.Shared.Physics;
using Content.Shared.Storage;
using Robust.Shared.Physics;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Impacts;

/// <summary>
///     The surfaces are given their materials by partials on upstream prototypes (Prototypes/_KS14/Impacts/Partials). A
///         partial that stopped applying, or a child's that its parent's overrode, looks like nothing was ever set.
/// </summary>
[TestFixture]
public sealed class KsImpactSurfacePartialsTest : GameTest
{
    private static readonly EntProtoId WallSolid = "WallSolid";
    private static readonly EntProtoId WallWood = "WallWood";
    private static readonly EntProtoId WallRockSnow = "WallRockSnow";
    private static readonly EntProtoId ReinforcedWindow = "ReinforcedWindow";
    private static readonly EntProtoId Bookshelf = "Bookshelf";

    private static readonly ProtoId<KsImpactMaterialPrototype> Metal = "Metal";
    private static readonly ProtoId<KsImpactMaterialPrototype> Wood = "Wood";
    private static readonly ProtoId<KsImpactMaterialPrototype> Snow = "Snow";
    private static readonly ProtoId<KsImpactMaterialPrototype> Glass = "Glass";
    private static readonly ProtoId<KsImpactMaterialPrototype> Paper = "Paper";

    [Test]
    public async Task SurfacesHaveTheirMaterials()
    {
        await Server.WaitAssertion(() =>
        {
            string MaterialOf(EntProtoId id) =>
                Server.ProtoMan.Index(id).TryComp<KsImpactSurfaceComponent>(out var surfaceComponent, SEntMan.ComponentFactory)
                    ? surfaceComponent.Material.Id
                    : "none";

            Assert.Multiple(() =>
            {
                Assert.That(MaterialOf(WallSolid), Is.EqualTo(Metal.Id), "a steel wall does not spark");
                Assert.That(MaterialOf(WallWood), Is.EqualTo(Wood.Id), "a wooden wall kept its parent's metal");
                Assert.That(MaterialOf(WallRockSnow), Is.EqualTo(Snow.Id), "a snowdrift kept its parent's rock");
                Assert.That(MaterialOf(ReinforcedWindow), Is.EqualTo(Glass.Id), "a reinforced window does not throw glass");
                Assert.That(MaterialOf(Bookshelf), Is.EqualTo(Wood.Id), "an empty bookshelf does not throw wood");

                var bookshelf = Server.ProtoMan.Index(Bookshelf);
                Assert.That(bookshelf.TryComp<KsImpactSurfaceStorageFillComponent>(out var storageFillComponent, SEntMan.ComponentFactory) ? storageFillComponent.Material.Id : "none",
                    Is.EqualTo(Paper.Id), "a bookshelf's books are not paper");
                Assert.That(bookshelf.TryComp<StorageComponent>(out _, SEntMan.ComponentFactory), "a bookshelf has no storage to fill");

                // Bullets and lasers stop at a bookshelf, or none of this is ever seen.
                Assert.That(bookshelf.TryComp<FixturesComponent>(out var fixturesComponent, SEntMan.ComponentFactory), "a bookshelf has no fixtures");
                var layer = fixturesComponent!.Fixtures.Values.Aggregate(0, (mask, fixture) => mask | fixture.CollisionLayer);
                Assert.That(layer & (int)CollisionGroup.BulletImpassable, Is.Not.Zero, "bullets pass through a bookshelf");
                Assert.That(layer & (int)CollisionGroup.Opaque, Is.Not.Zero, "lasers pass through a bookshelf");
            });
        });
    }

    /// <summary>
    ///     Every material's debris bounces off walls, through the collision its abstract base gives them all; a child
    ///         that restated another field must not have lost it.
    /// </summary>
    [Test]
    public async Task ImpactDebrisCollidesWithWalls()
    {
        await Server.WaitAssertion(() =>
        {
            var debrisPrototypes = Server.ProtoMan.EnumeratePrototypes<KsParticlesPrototype>()
                .Where(prototype => prototype.Parents?.Contains("KsImpactDebrisBase") == true)
                .ToList();

            Assert.That(debrisPrototypes, Is.Not.Empty, "no impact debris prototypes found, so this checks nothing");
            Assert.Multiple(() =>
            {
                foreach (var prototype in debrisPrototypes)
                    Assert.That(prototype.Collision is { Enabled: true }, $"{prototype.ID} flies through walls");
            });
        });
    }
}
