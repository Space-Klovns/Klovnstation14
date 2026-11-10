using System.Linq;
using Content.Shared._KS14.Fluidmos.Reactions;
using Content.Shared.Chemistry.Reagent;

namespace Content.Client.Guidebook.Controls;

/// <summary>
///     Puts reactions that run in pipes on a reagent's card: those that make it with its other recipes, and those that
///         use or need it in a section of their own, since the only way to learn what a refinery cut is good for is here.
/// </summary>
public sealed partial class GuideReagentEmbed
{
    private void KsGenerateFluidReactions(ReagentPrototype reagent)
    {
        var usedInPipes = false;

        var reactions = _prototype.EnumeratePrototypes<KsFluidReactionPrototype>()
            .Where(reaction => KsMakes(reaction, reagent) || KsNeeds(reaction, reagent))
            .OrderBy(reaction => reaction.MinTemp)
            .ThenBy(reaction => reaction.ID);

        foreach (var reaction in reactions)
        {
            if (KsMakes(reaction, reagent))
            {
                RecipesDescriptionContainer.AddChild(new GuideReagentReaction(reaction, _prototype, _systemManager));
                RecipesContainer.Visible = true;
            }

            if (KsNeeds(reaction, reagent))
            {
                KsFluidReactionsDescriptionContainer.AddChild(new GuideReagentReaction(reaction, _prototype, _systemManager));
                usedInPipes = true;
            }
        }

        KsFluidReactionsContainer.Visible = usedInPipes;
    }

    private static bool KsMakes(KsFluidReactionPrototype reaction, ReagentPrototype reagent)
    {
        return reaction.Products.ContainsKey(reagent.ID);
    }

    /// <summary>
    ///     Whether a reaction uses a reagent, or only needs it there: a catalyst in the pipe, or a partial pressure of it.
    /// </summary>
    private static bool KsNeeds(KsFluidReactionPrototype reaction, ReagentPrototype reagent)
    {
        return reaction.Reactants.ContainsKey(reagent.ID) ||
               reaction.CatalystReagent == reagent.ID ||
               reaction.MinPartialPressure.ContainsKey(reagent.ID);
    }
}
