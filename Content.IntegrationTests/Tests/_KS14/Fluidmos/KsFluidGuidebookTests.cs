#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Content.Client._KS14.Guidebook.Controls;
using Content.Client.Guidebook.Controls;
using Content.Client.UserInterface.ControlExtensions;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Fluidmos.Reactions;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Reactions that run in pipes in the guidebook: on the cards of the reagents they make and use, and on the Oil
///         Refining page, which is the only other way a player learns how to run them.
/// </summary>
[TestOf(typeof(GuideReagentEmbed))]
public sealed class KsFluidGuidebookTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    private const string RefiningPage = "/ServerInfo/_KS14/Guidebook/Engineering/Refining.xml";
    private const string RussianRefiningPage = "/ServerInfo/_KS14/Guidebook/ru-RU/_KS14/Guidebook/Engineering/Refining.xml";

    private const string TestReaction = "KsTestGuideReaction";

    private static readonly Regex EmbedRegex = new("<KsGuideFluidReactionEmbed Reactions=\"([^\"]*)\"", RegexOptions.Compiled);

    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestGuideFeed
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: KsTestGuideMade
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: KsTestGuideBystander
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: KsTestGuideCatalystReagent
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: KsTestGuidePressureReagent
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: ksFluidReaction
  id: KsTestGuideReaction
  reactants:
    KsTestGuideFeed: { amount: 1 }
  products:
    KsTestGuideMade: { amount: 1 }
  minTemp: 123.5
  maxTemp: 456.5
  catalyst: KsPlatinum
  catalystReagent: KsTestGuideCatalystReagent
  minPartialPressure:
    KsTestGuidePressureReagent: 1
";

    private readonly ProtoId<KsFluidReactionPrototype> _reaction = TestReaction;
    private readonly ProtoId<ReagentPrototype> _feedReagent = "KsTestGuideFeed";
    private readonly ProtoId<ReagentPrototype> _madeReagent = "KsTestGuideMade";
    private readonly ProtoId<ReagentPrototype> _bystanderReagent = "KsTestGuideBystander";
    private readonly ProtoId<ReagentPrototype> _catalystReagent = "KsTestGuideCatalystReagent";
    private readonly ProtoId<ReagentPrototype> _pressureReagent = "KsTestGuidePressureReagent";

    private const string ReactionTemperature = "123.5";

    /// <summary>
    ///     Collapsed sections stay in the tree, so a section's own visibility is what says the card offers it.
    /// </summary>
    private static bool HasReactionIn(GuideReagentEmbed card, string section, string sectionBody)
    {
        return Section(card, section).Visible && Section(card, sectionBody).ChildrenContainText(ReactionTemperature);
    }

    private static Control Section(GuideReagentEmbed card, string name)
    {
        return card.FindControl<Control>(name);
    }

    [Test]
    public async Task ACardShowsWhatMakesItAndWhatItIsUsedFor()
    {
        var client = Pair.Client;
        var protoMan = client.ProtoMan;

        await client.WaitAssertion(() =>
        {
            var feed = new GuideReagentEmbed(protoMan.Index(_feedReagent));
            var made = new GuideReagentEmbed(protoMan.Index(_madeReagent));
            var bystander = new GuideReagentEmbed(protoMan.Index(_bystanderReagent));
            var catalyst = new GuideReagentEmbed(protoMan.Index(_catalystReagent));
            var pressure = new GuideReagentEmbed(protoMan.Index(_pressureReagent));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(HasReactionIn(made, "RecipesContainer", "RecipesDescriptionContainer"),
                    "A product's card doesn't list the pipe reaction as a recipe.");
                Assert.That(Section(made, "KsFluidReactionsContainer").Visible, Is.False,
                    "A product's card offers a section of reactions that use it, with none to show.");

                Assert.That(HasReactionIn(feed, "KsFluidReactionsContainer", "KsFluidReactionsDescriptionContainer"),
                    "A reactant's card doesn't say what it reacts into.");
                Assert.That(Section(feed, "RecipesContainer").Visible, Is.False,
                    "A reactant's card shows a recipe, though nothing makes it.");

                Assert.That(Section(bystander, "RecipesContainer").Visible || Section(bystander, "KsFluidReactionsContainer").Visible, Is.False,
                    "A reagent with nothing to do with the reaction lists it.");

                Assert.That(HasReactionIn(catalyst, "KsFluidReactionsContainer", "KsFluidReactionsDescriptionContainer"),
                    "A reagent a reaction needs in the pipe doesn't say so on its card.");
                Assert.That(HasReactionIn(pressure, "KsFluidReactionsContainer", "KsFluidReactionsDescriptionContainer"),
                    "A reagent a reaction needs a pressure of doesn't say so on its card.");
            }

            feed.Dispose();
            made.Dispose();
            bystander.Dispose();
            catalyst.Dispose();
            pressure.Dispose();
        });
    }

    [Test]
    public async Task AReactionEmbedShowsWhatItTakesToRun()
    {
        var client = Pair.Client;
        var localizationManager = client.ResolveDependency<ILocalizationManager>();

        await client.WaitAssertion(() =>
        {
            var embed = new KsGuideFluidReactionEmbed();
            Assert.That(embed.TryParseTag(new Dictionary<string, string> { ["Reactions"] = TestReaction }, out _),
                "The embed refused a real reaction.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(embed.ChildrenContainText(ReactionTemperature), "The embed doesn't give the temperatures.");
                Assert.That(embed.ChildrenContainText(localizationManager.GetString("ks-fluid-catalyst-name-platinum")),
                    "The embed doesn't name the bed.");
            }

            embed.Dispose();
        });
    }

    /// <summary>
    ///     The conditions of a pipe reaction are long lines. At a guidebook's width they wrap inside their card instead of
    ///         running out of its right side.
    /// </summary>
    [Test]
    public async Task ReactionConditionsStayInsideTheirCard([Values(360f, 480f)] float cardWidth)
    {
        var client = Pair.Client;
        var protoMan = client.ProtoMan;
        var systemManager = client.ResolveDependency<IEntitySystemManager>();

        await client.WaitAssertion(() =>
        {
            var card = new GuideReagentReaction(protoMan.Index(_reaction), protoMan, systemManager);
            card.Measure(new Vector2(cardWidth, float.PositiveInfinity));
            card.Arrange(new UIBox2(0f, 0f, cardWidth, card.DesiredSize.Y));

            var label = card.MixLabel;
            var right = label.GlobalPosition.X + label.Size.X;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(right, Is.LessThanOrEqualTo(cardWidth + 0.5f), "The conditions run out of the card's right side.");
                Assert.That(label.Size.X, Is.LessThanOrEqualTo(GuideReagentReaction.KsFluidConditionsMaxWidth + 3f),
                    "The conditions don't wrap.");
            }

            card.Dispose();
        });
    }

    /// <summary>
    ///     A reaction left off the page can't be worked out in play, so a new one fails here until it's documented.
    /// </summary>
    [Test]
    public async Task EveryReactionIsOnTheRefiningPage([Values(RefiningPage, RussianRefiningPage)] string page)
    {
        var client = Pair.Client;
        var protoMan = client.ProtoMan;
        var resourceManager = client.ResolveDependency<IResourceManager>();

        await client.WaitAssertion(() =>
        {
            using var reader = resourceManager.ContentFileReadText(new ResPath(page));
            var text = reader.ReadToEnd();

            var embedded = EmbedRegex.Matches(text)
                .SelectMany(match => match.Groups[1].Value.Split(','))
                .ToHashSet();

            using (Assert.EnterMultipleScope())
            {
                foreach (var reaction in protoMan.EnumeratePrototypes<KsFluidReactionPrototype>())
                {
                    // Every fixture's test prototypes are loaded alongside the real ones.
                    if (!reaction.ID.StartsWith("KsTest", StringComparison.Ordinal))
                        Assert.That(embedded, Does.Contain(reaction.ID), $"{page} doesn't show {reaction.ID}.");
                }
            }
        });
    }
}
