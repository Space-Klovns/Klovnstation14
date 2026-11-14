#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Construction.Prototypes;
using Content.Client.Guidebook.Components;
using Content.Shared.Guidebook;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
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
    private static readonly EntProtoId Sandbags = "KsSandbag";
    private static readonly ResPath SandbagItemRsi = new("/Textures/_KS14/Objects/Misc/sandbag.rsi");
    private static readonly ProtoId<GuideEntryPrototype> FortificationsGuide = "KsFortifications";
    private static readonly ProtoId<GuideEntryPrototype> ConstructionGuide = "Construction";
    private static readonly EntProtoId[] Fortifications = ["KsSandbagWall", "KsSandbag", "KsBarbedWire", "KsBarbedWireCoil"];
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

    /// <summary>
    ///     Sandbags lying about look like bags, not like the wall they build, and the pile grows with the stack: a full
    ///         stack shows the biggest pile, a single bag the smallest.
    /// </summary>
    [Test]
    public async Task SandbagPileGrowsWithTheStack()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid sandbagsUid = default;
        await Server.WaitPost(() => sandbagsUid = SSpawnAtPosition(Sandbags, testMap.GridCoords));
        await Pair.RunTicksSync(5);

        var states = new List<string>();
        async Task ReadState()
        {
            await Client.WaitPost(() =>
            {
                var clientUid = ToClientUid(sandbagsUid);
                var spriteComponent = CComp<SpriteComponent>(clientUid);
                Assert.That(Client.System<SpriteSystem>().LayerMapTryGet((clientUid, spriteComponent), "base", out var index, logMissing: false),
                    "the sandbags should have a base layer for the stack to change");
                var layer = spriteComponent[index];
                Assert.That(layer.ActualRsi?.Path, Is.EqualTo(SandbagItemRsi), "sandbags should use the sandbag item sprite, not the wall's");
                states.Add(layer.RsiState.Name ?? string.Empty);
            });
        }

        await ReadState();
        await Server.WaitPost(() => Server.System<SharedStackSystem>().SetCount(sandbagsUid, 1));
        await Pair.RunTicksSync(5);
        await ReadState();

        Assert.That(states[0], Is.Not.EqualTo(states[1]), "a single bag should look smaller than a full stack");
    }

    /// <summary>
    ///     The fortifications guide hangs under Construction, and examining a fortification's guidebook button opens it.
    /// </summary>
    [Test]
    public async Task FortificationsHaveAGuideUnderConstruction()
    {
        await Client.WaitAssertion(() =>
        {
            Assert.That(CProtoMan.HasIndex(FortificationsGuide), "the fortifications guide should exist");
            Assert.That(CProtoMan.Index(ConstructionGuide).Children, Does.Contain(FortificationsGuide),
                "the fortifications guide should be listed under Construction");

            Assert.Multiple(() =>
            {
                foreach (var fortification in Fortifications)
                {
                    Assert.That(CProtoMan.Index(fortification).TryComp<GuideHelpComponent>(out var guideHelpComponent, CEntMan.ComponentFactory),
                        $"{fortification} should link to a guide");
                    Assert.That(guideHelpComponent?.Guides, Does.Contain(FortificationsGuide), $"{fortification} should link to the fortifications guide");
                }
            });
        });
    }
}
