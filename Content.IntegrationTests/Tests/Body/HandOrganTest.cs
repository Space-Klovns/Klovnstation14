using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Klovnmed; // KS14
using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Hands.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(HandOrganSystem))]
public sealed class HandOrganTest : GameTest
{
    // KS14: Modified prototype to match hierarchy
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
        - id: LeftArm
        - id: RightArm

- type: entity
  id: LeftArm
  components:
  - type: Organ
    category: ArmLeft
  - type: EntityTableContainerFill
    containers:
      body_organs: !type:AllSelector
        children:
        - id: LeftHand

- type: entity
  id: RightArm
  components:
  - type: Organ
    category: ArmRight
  - type: EntityTableContainerFill
    containers:
      body_organs: !type:AllSelector
        children:
        - id: RightHand

- type: entity
  id: LeftHand
  components:
  - type: Organ
    category: HandLeft
  - type: HandOrgan
    handID: left
    data:
      location: Left

- type: entity
  id: RightHand
  components:
  - type: Organ
    category: HandRight
  - type: HandOrgan
    handID: right
    data:
      location: Right
";
    [Test]
    public async Task HandInsertionAndRemovalTest()
    {
        var pair = Pair;
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
            EntityUid[] contained = [bodyComponent.PresentOrganCategories["HandLeft"], bodyComponent.PresentOrganCategories["HandRight"]]; // KS14: Use hierarchy instead of container
            foreach (var hand in contained)
            {
                expectedCount--;
                entityManager.DeleteEntity(hand); // KS14
                Assert.That(hands.Count, Is.EqualTo(expectedCount));
            }

            var protos = new List<(string, string)>() { ("ArmLeft", "LeftHand"), ("ArmRight", "RightHand") }; // KS14: Use hierarchy instead of container
            foreach (var (parentCategory, proto) in protos) // KS14
            {
                expectedCount++;
                entityManager.SpawnInContainerOrDrop(proto, bodyComponent.PresentOrganCategories[parentCategory], BodyHierarchySystem.ConstContainerId); // KS14: Use hierarchy instead of container
                Assert.That(hands.Count, Is.EqualTo(expectedCount));
            }
        });
    }
}
