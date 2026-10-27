#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared._KS14.Trail;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.SpawnMenu;

/// <summary>
///     Entities that are only ever made by a system are hidden from the spawn menu by a category their component implies,
///         rather than each prototype listing <c>HideSpawnMenu</c>. That category does not inherit, so a variant that
///         forgets it shows up in the menu.
/// </summary>
[TestFixture]
public sealed class KsSpawnMenuCategoryTest : GameTest
{
    /// <summary>
    ///     Checked on the client, which builds the spawn menu: a category implied by a server-only component never
    ///         reaches it.
    /// </summary>
    [TestCase(typeof(KsTrailComponent))]
    [TestCase(typeof(KsFoamBlanketComponent))]
    public async Task TestEveryPrototypeWithTheComponentIsHidden(System.Type componentType)
    {
        var clientPrototypeManager = Client.ResolveDependency<IPrototypeManager>();
        var componentName = Client.ResolveDependency<IComponentFactory>().GetComponentName(componentType);

        var prototypes = clientPrototypeManager.EnumeratePrototypes<EntityPrototype>()
            .Where(prototype => !prototype.Abstract && prototype.Components.ContainsKey(componentName))
            .ToList();

        Assert.That(prototypes, Is.Not.Empty, $"No prototype has {componentName}, so this checks nothing.");

        Assert.Multiple(() =>
        {
            foreach (var prototype in prototypes)
            {
                Assert.That(prototype.HideSpawnMenu, $"{prototype.ID} has {componentName}, so it should be hidden from the spawn menu.");
            }
        });

        await Task.CompletedTask;
    }
}
