#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     How the field fortifications are presented to players: their names in the construction menu, the sandbag item's
///         sprite and their guidebook entry.
/// </summary>
[TestFixture]
public sealed class KsFortificationsContentTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly ProtoId<ConstructionPrototype> BarbedWireRecipe = "KsBarbedWire";
    private static readonly ProtoId<ConstructionPrototype> SandbagWallRecipe = "KsSandbagWall";
    private static readonly ProtoId<ConstructionPrototype>[] Recipes = [BarbedWireRecipe, SandbagWallRecipe];

    /// <summary>
    ///     The construction menu lists a recipe by the name the client works out for it when it starts.
    /// </summary>
    [Test]
    public async Task RecipesHaveNamesInTheConstructionMenu()
    {
        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var recipe in Recipes)
                {
                    var name = CProtoMan.Index(recipe).Name;
                    Assert.That(name, Is.Not.Null.And.Not.Empty, $"the {recipe} recipe should have a name in the construction menu");
                    TestContext.Out.WriteLine($"{recipe}: '{name}'");
                }
            });
        });
    }

    /// <summary>
    ///     Editing a recipe's file while the game runs reloads its prototype, which starts without a name: the client has
    ///         to work it out again, or the recipe is listed blank until a restart.
    /// </summary>
    [Test]
    public async Task RecipeKeepsItsNameThroughAHotReload()
    {
        await Client.WaitPost(() =>
        {
            var recipe = CProtoMan.Index(BarbedWireRecipe);
            var changes = new Dictionary<Type, HashSet<string>>();
            CProtoMan.LoadString($@"
- type: construction
  id: {recipe.ID}
  graph: {recipe.Graph}
  startNode: {recipe.StartNode}
  targetNode: {recipe.TargetNode}
  category: {recipe.Category}
  objectType: {recipe.Type}
  placementMode: {recipe.PlacementMode}
", overwrite: true, changed: changes);
            CProtoMan.ResolveResults();
            CProtoMan.ReloadPrototypes(changes);
        });

        await Client.WaitAssertion(() =>
        {
            var reloaded = CProtoMan.Index(BarbedWireRecipe);
            Assert.That(reloaded.Name, Is.Not.Null.And.Not.Empty, "a reloaded recipe should still have a name in the construction menu");
            Assert.That(Client.System<Content.Client.Construction.ConstructionSystem>().TryGetRecipePrototype(reloaded.ID, out _),
                "a reloaded recipe should still show what it builds");
        });
    }
}
