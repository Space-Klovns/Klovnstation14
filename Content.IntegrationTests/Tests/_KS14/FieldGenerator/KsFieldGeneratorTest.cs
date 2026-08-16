using Content.IntegrationTests.Fixtures;
using Content.Server.Power.Components;
using Content.Shared._KS14.FieldGenerator;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.FieldGenerator;

/// <summary>
///     Regression tests for stale one-way generator links.
///     Linking a third generator to an already-linked one used to overwrite the
///         existing link, orphaning the old partner with a one-way reference to a
///         generator that no longer points back. Unanchoring or deleting the orphan
///         after its stale partner was deleted then crashed the server with a
///         KeyNotFoundException inside UnlinkAndClearFields, which took down whole
///         integration test runs when maps with stacked generators got flushed.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldGeneratorSystem))]
public sealed class KsFieldGeneratorTest : GameTest
{
    private const string GeneratorProto = "KsAtmosShieldGenEnabled";

    /// <summary>
    ///     Mirrors the Nanoplant setup: two generators stacked on one tile facing
    ///         opposite ways, plus a third generator further out facing one of them.
    ///         The third generator powering up must not steal the existing link.
    /// </summary>
    [Test]
    public async Task TestLinkingDoesNotOrphanExistingLink()
    {
        var pair = Pair;
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entityMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSys = entityMan.System<SharedMapSystem>();
        var transformSys = entityMan.System<SharedTransformSystem>();

        Entity<MapGridComponent> grid = default;
        EntityUid first = default;
        EntityUid second = default;
        EntityUid third = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);

            for (var y = 0; y <= 2; ++y)
            {
                mapSys.SetTile(grid, grid, new Vector2i(0, y), new Tile(1));
            }

            // Same-tile pair: first faces north, second faces south.
            first = entityMan.SpawnEntity(GeneratorProto, new EntityCoordinates(grid, 0.5f, 0.5f));
            transformSys.SetLocalRotation(first, Angle.FromDegrees(180));

            second = entityMan.SpawnEntity(GeneratorProto, new EntityCoordinates(grid, 0.5f, 0.5f));
            transformSys.SetLocalRotation(second, Angle.Zero);

            // Two tiles north, facing south back at the pair.
            third = entityMan.SpawnEntity(GeneratorProto, new EntityCoordinates(grid, 0.5f, 2.5f));
            transformSys.SetLocalRotation(third, Angle.Zero);
        });

        await server.WaitRunTicks(5);

        // Power the pair one after the other so the second one's power-up scan links them.
        await PowerOn(first);
        await PowerOn(second);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetLink(first), Is.EqualTo(second), "Same-tile generators did not link up");
                Assert.That(GetLink(second), Is.EqualTo(first), "Same-tile link was not symmetric");
            });
        });

        // The third generator can see the first one, but the first one is taken.
        await PowerOn(third);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetLink(first), Is.EqualTo(second), "Existing link was overwritten by a third generator");
                Assert.That(GetLink(second), Is.EqualTo(first), "Existing link partner was orphaned by a third generator");
                Assert.That(GetLink(third), Is.Null, "Third generator linked to an already-linked generator");
            });
        });

        // Deleting the linked pair and the leftover generator must clean up without throwing.
        await server.WaitPost(() => entityMan.DeleteEntity(first));
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetLink(second), Is.Null, "Deleted generator did not clear its partner's link");
        });

        await server.WaitPost(() =>
        {
            entityMan.DeleteEntity(second);
            entityMan.DeleteEntity(third);
        });

        await server.WaitRunTicks(3);

        async Task PowerOn(EntityUid uid)
        {
            await server.WaitAssertion(() =>
            {
                entityMan.GetComponent<ApcPowerReceiverComponent>(uid).NeedsPower = false;
            });

            await server.WaitRunTicks(5);
        }

        EntityUid? GetLink(EntityUid uid) =>
            entityMan.GetComponent<KsFieldGeneratorComponent>(uid).LinkedGeneratorUid;
    }

    /// <summary>
    ///     Deleting a generator whose (corrupted) one-way link points at an
    ///         already-deleted generator must not throw. This is the exact state old
    ///         link overwrites left behind, staged directly as defense in depth.
    /// </summary>
    [Test]
    public async Task TestStaleLinkedGeneratorDeletionDoesNotThrow()
    {
        var pair = Pair;
        var server = pair.Server;

        var testMap = await pair.CreateTestMap();

        var entityMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSys = entityMan.System<SharedMapSystem>();

        Entity<MapGridComponent> grid = default;
        EntityUid orphan = default;
        EntityUid stalePartner = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);
            mapSys.SetTile(grid, grid, Vector2i.Zero, new Tile(1));
            mapSys.SetTile(grid, grid, new Vector2i(1, 0), new Tile(1));

            orphan = entityMan.SpawnEntity(GeneratorProto, new EntityCoordinates(grid, 0.5f, 0.5f));
            stalePartner = entityMan.SpawnEntity(GeneratorProto, new EntityCoordinates(grid, 1.5f, 0.5f));

            // Stage the corrupted one-way link (the partner never points back).
            //     KsFieldGeneratorComponent restricts writes via [Access], so the test
            //     has to stage this broken state through reflection.
            typeof(KsFieldGeneratorComponent)
                .GetField(nameof(KsFieldGeneratorComponent.LinkedGeneratorUid))!
                .SetValue(entityMan.GetComponent<KsFieldGeneratorComponent>(orphan), (EntityUid?)stalePartner);
        });

        await server.WaitRunTicks(3);

        // Delete the partner first; its shutdown does not know about the one-way link,
        //     so the orphan keeps pointing at a dead entity.
        await server.WaitPost(() => entityMan.DeleteEntity(stalePartner));
        await server.WaitRunTicks(3);

        // Deleting (and thereby unanchoring) the orphan used to throw KeyNotFoundException.
        await server.WaitPost(() => entityMan.DeleteEntity(orphan));
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entityMan.Deleted(orphan), "Orphaned generator was not deleted");
                Assert.That(entityMan.Deleted(stalePartner), "Stale partner was not deleted");
            });
        });
    }
}
