#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server.Engineering.Components;
using Content.Shared._KS14.Construction.TilePlacement;
using Content.Shared.Item;
using Content.Shared.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Construction;

/// <summary>
///     Upstream's inflatable barricade and door are built by clicking a tile through a partial
///         (<c>_KS14/Entities/Objects/Tools/Partials/inflatable_wall.yml</c>): it swaps their server-only
///         <c>SpawnAfterInteract</c> for <see cref="KsPlaceOnTileComponent"/>, building what they built before, and
///         leaves the rest of upstream's prototype alone. A partial that stopped applying looks exactly like upstream.
/// </summary>
[TestFixture]
public sealed class KsPlaceOnTileInflatablePartialTest : GameTest
{
    private static readonly EntProtoId InflatableWallStack = "InflatableWallStack";
    private static readonly EntProtoId InflatableWallStackSingle = "InflatableWallStack1";
    private static readonly EntProtoId InflatableDoorStack = "InflatableDoorStack";
    private static readonly EntProtoId InflatableWall = "InflatableWall";
    private static readonly EntProtoId InflatableDoor = "InflatableDoor";
    private static readonly ProtoId<StackPrototype> InflatableWallStackType = "InflatableWall";
    private static readonly ProtoId<StackPrototype> InflatableDoorStackType = "InflatableDoor";
    private static readonly ProtoId<TagPrototype> InflatableTag = "Inflatable";

    /// <summary>
    ///     Each inflatable, what it inflates into and its upstream stack type. The single barricade inherits it all.
    /// </summary>
    private static readonly (EntProtoId Item, EntProtoId Built, ProtoId<StackPrototype> StackType)[] Inflatables =
    [
        (InflatableWallStack, InflatableWall, InflatableWallStackType),
        (InflatableWallStackSingle, InflatableWall, InflatableWallStackType),
        (InflatableDoorStack, InflatableDoor, InflatableDoorStackType),
    ];

    [Test]
    public void InflatablesArePlacedOnTileAndKeepUpstreamData()
    {
        Assert.Multiple(() =>
        {
            foreach (var (item, built, stackType) in Inflatables)
            {
                // Both sides: the client predicts the build from its own copy of the prototype.
                foreach (var (side, protoMan, factory) in new[] { ("server", SProtoMan, SEntMan.ComponentFactory), ("client", CProtoMan, CEntMan.ComponentFactory) })
                {
                    var prototype = protoMan.Index<EntityPrototype>(item);
                    Assert.That(prototype.TryGetComponent<KsPlaceOnTileComponent>(out var placeOnTile, factory),
                        $"{item} should be built by clicking a tile on the {side}");
                    Assert.That(placeOnTile?.Prototype.Id, Is.EqualTo(built.Id), $"{item} should still build {built} on the {side}");

                    Assert.That(prototype.TryGetComponent<StackComponent>(out var stackComponent, factory), $"{item} should keep upstream's stack on the {side}");
                    Assert.That(stackComponent?.StackTypeId.Id, Is.EqualTo(stackType.Id), $"{item} should keep upstream's stack type on the {side}");
                    Assert.That(prototype.HasComponent<ItemComponent>(factory), $"{item} should still be an item on the {side}");
                    Assert.That(prototype.TryGetComponent<TagComponent>(out var tagComponent, factory) && Server.System<TagSystem>().HasTag(tagComponent, InflatableTag),
                        $"{item} should keep upstream's tags on the {side}");
                }

                Assert.That(SProtoMan.Index<EntityPrototype>(item).HasComponent<SpawnAfterInteractComponent>(SEntMan.ComponentFactory), Is.False,
                    $"{item} shouldn't also spawn through upstream's SpawnAfterInteract");
            }
        });
    }
}
