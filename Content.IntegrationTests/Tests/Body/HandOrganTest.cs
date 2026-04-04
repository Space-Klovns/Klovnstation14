using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.Body;
using Content.Shared.Hands.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(HandOrganSystem))]
public sealed class HandOrganTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TheBody
  components:
  - type: Body
  - type: Hands
  - type: EntityTableContainerFill
    containers:
      body_organs: !type:AllSelector
        children:
        - id: LeftHand
        - id: RightHand

- type: entity
  id: LeftHand
  components:
  - type: Organ
  - type: HandOrgan
    handID: left
    data:
      location: Left

- type: entity
  id: RightHand
  components:
  - type: Organ
  - type: HandOrgan
    handID: right
    data:
      location: Right
";
    [Test]
    public async Task HandInsertionAndRemovalTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitIdleAsync();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var mapData = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var container = entityManager.System<SharedContainerSystem>();
            var body = entityManager.SpawnEntity("TheBody", mapData.GridCoords);
            var hands = entityManager.GetComponent<HandsComponent>(body);
            var bodyComponent = entityManager.GetComponent<BodyComponent>(body); // KS14

            Assert.That(hands.Count, Is.EqualTo(2));

            // KS14: Use hierarchy instead of container

            var expectedCount = 2;
            var contained = bodyComponent.RecursiveChildUids; // KS14: Use hierarchy instead of container
            foreach (var hand in contained)
            {
                expectedCount--;
                entityManager.DeleteEntity(hand); // KS14
                Assert.That(hands.Count, Is.EqualTo(expectedCount));
            }

            var protos = new List<string>() { "LeftHand", "RightHand" };
            foreach (var proto in protos)
            {
                expectedCount++;
                entityManager.SpawnAttachedTo(proto, new(body, Vector2.Zero)); // KS14: Use hierarchy instead of container
                Assert.That(hands.Count, Is.EqualTo(expectedCount));
            }
        });

        await pair.CleanReturnAsync();
    }
}
