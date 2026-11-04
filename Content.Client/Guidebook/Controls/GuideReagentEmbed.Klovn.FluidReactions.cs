using System.Linq;
using Content.Shared._KS14.Fluidmos.Reactions;
using Content.Shared.Chemistry.Reagent;

namespace Content.Client.Guidebook.Controls;

/// <summary>
///     Puts reactions that run in pipes on a reagent's card: those that make it with its other recipes, and those that
///         use it in a section of their own, since the only way to learn what a refinery cut is good for is here.
/// </summary>
public sealed partial class GuideReagentEmbed
{
    private void KsGenerateFluidReactions(ReagentPrototype reagent)
    {
        var usedInPipes = false;

        var reactions = _prototype.EnumeratePrototypes<KsFluidReactionPrototype>()
            .OrderBy(reaction => reaction.MinTemp)
            .ThenBy(reaction => reaction.ID);

        foreach (var reaction in reactions)
        {
            if (reaction.Products.ContainsKey(reagent.ID))
            {
                RecipesDescriptionContainer.AddChild(new GuideReagentReaction(reaction, _prototype, _systemManager));
                RecipesContainer.Visible = true;
            }

            if (reaction.Reactants.ContainsKey(reagent.ID))
            {
                KsFluidReactionsDescriptionContainer.AddChild(new GuideReagentReaction(reaction, _prototype, _systemManager));
                usedInPipes = true;
            }
        }

        KsFluidReactionsContainer.Visible = usedInPipes;
    }
}
