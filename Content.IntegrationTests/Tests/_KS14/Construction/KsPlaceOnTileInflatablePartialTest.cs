#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.Engineering.Components;
using Content.Shared._KS14.Construction.TilePlacement;
using Content.Shared.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Construction;

/// <summary>
///     Upstream's inflatable barricade and door are built by clicking a tile through a partial
///         (<c>_KS14/Entities/Objects/Tools/Partials/inflatable_wall.yml</c>) that swaps their server-only
///         <c>SpawnAfterInteract</c> for <see cref="KsPlaceOnTileComponent"/>. A partial that stopped applying looks
///         exactly like upstream, so check the merge took and upstream's own data is still there.
/// </summary>
[TestFixture]
public sealed class KsPlaceOnTileInflatablePartialTest : GameTest
{
    private static readonly EntProtoId InflatableWallStack = "InflatableWallStack";
    private static readonly EntProtoId InflatableDoorStack = "InflatableDoorStack";

    [Test]
    public void InflatablesArePlacedOnTileAndKeepUpstreamData()
    {
        Assert.Multiple(() =>
        {
            foreach (var item in new[] { InflatableWallStack, InflatableDoorStack })
            {
                // Both sides: the client predicts the build from its own copy of the prototype.
                foreach (var (side, protoMan, factory) in new[] { ("server", SProtoMan, SEntMan.ComponentFactory), ("client", CProtoMan, CEntMan.ComponentFactory) })
                {
                    var prototype = protoMan.Index(item);
                    Assert.That(prototype.TryGetComponent<KsPlaceOnTileComponent>(out var placeOnTile, factory),
                        $"{item} should be built by clicking a tile on the {side}");
                    Assert.That(placeOnTile != null && protoMan.HasIndex(placeOnTile.Prototype),
                        $"{item} should build something that exists on the {side}");
                    Assert.That(prototype.HasComponent<StackComponent>(factory), $"{item} should keep upstream's stack on the {side}");
                }

                Assert.That(SProtoMan.Index(item).HasComponent<SpawnAfterInteractComponent>(SEntMan.ComponentFactory), Is.False,
                    $"{item} shouldn't also spawn through upstream's SpawnAfterInteract");
            }
        });
    }
}
