using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.IntegrationTests.Tests._KS14.ChemicalFire;

/// <summary>
///     Every chemfire prototype points at art that exists. Chemfires have no sprite component, so nothing else
///         checks their RSI: a wrong path, prefix or variation count would only show as a fire that is invisible.
/// </summary>
[TestFixture]
public sealed class ChemicalFireSpriteTest : GameTest
{
    private static readonly string[] ConnectionSuffixes = ["", "-west", "-east", "-full"];

    [Test]
    public async Task TestEveryChemicalFireHasEveryStateItCanDraw()
    {
        var client = Pair.Client;
        var prototypeManager = client.ResolveDependency<IPrototypeManager>();
        var componentFactory = client.ResolveDependency<IComponentFactory>();
        var resourceCache = client.ResolveDependency<IResourceCache>();

        await client.WaitAssertion(() =>
        {
            var checkedPrototypes = 0;

            using (Assert.EnterMultipleScope())
            {
                foreach (var prototype in prototypeManager.EnumeratePrototypes<EntityPrototype>())
                {
                    if (prototype.Abstract ||
                        !prototype.TryGetComponent(out ChemicalFireComponent fireComponent, componentFactory))
                        continue;

                    checkedPrototypes++;

                    var rsiPath = SpriteSpecifierSerializer.TextureRoot / fireComponent.Sprite;
                    if (!resourceCache.TryGetResource<RSIResource>(rsiPath, out var rsiResource))
                    {
                        Assert.Fail($"{prototype.ID}: no RSI at {rsiPath}.");
                        continue;
                    }

                    for (var variation = 1; variation <= Math.Max(1, fireComponent.SpriteVariations); variation++)
                    {
                        foreach (var suffix in ConnectionSuffixes)
                        {
                            foreach (var prefix in new[] { fireComponent.UnderStatePrefix, fireComponent.OverStatePrefix })
                            {
                                var state = prefix + variation + suffix;
                                Assert.That(rsiResource.RSI.TryGetState(state, out _), Is.True,
                                    $"{prototype.ID}: {rsiPath} has no state '{state}'.");
                            }
                        }
                    }
                }
            }

            Assert.That(checkedPrototypes, Is.GreaterThan(0), "No chemfire prototypes were found to check.");
        });
    }
}
