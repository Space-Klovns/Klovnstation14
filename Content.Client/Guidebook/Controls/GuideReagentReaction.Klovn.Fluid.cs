using Content.Client.Message;
using Content.Shared._KS14.Fluidmos.Reactions;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Guidebook.Controls;

/// <summary>
///     Shows a reaction that runs in pipes rather than a beaker, with what it takes to run it.
/// </summary>
public sealed partial class GuideReagentReaction
{
    private static readonly ProtoId<MixingCategoryPrototype> KsFluidPipeMixingCategory = "KsFluidPipe";
    private static readonly ProtoId<MixingCategoryPrototype> KsFluidReactorMixingCategory = "KsFluidReactor";

    public GuideReagentReaction(KsFluidReactionPrototype prototype, IPrototypeManager protoMan, IEntitySystemManager sysMan) : this(protoMan)
    {
        Container reactantContainer = ReactantsContainer;
        SetReagents(KsFluidAmounts(prototype.Reactants), ref reactantContainer, protoMan);

        Container productContainer = ProductsContainer;
        SetReagents(KsFluidAmounts(prototype.Products), ref productContainer, protoMan, addLinks: false);

        var category = protoMan.Index(prototype.Catalyst != null ? KsFluidReactorMixingCategory : KsFluidPipeMixingCategory);
        MixTexture.Texture = sysMan.GetEntitySystem<SpriteSystem>().Frame0(category.Icon);
        MixLabel.SetMarkup(string.Join('\n', KsFluidConditions(prototype, category, protoMan)));
    }

    private static Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> KsFluidAmounts(Dictionary<ProtoId<ReagentPrototype>, KsFluidReactionReagent> reagents)
    {
        var amounts = new Dictionary<ProtoId<ReagentPrototype>, FixedPoint2>();
        foreach (var (reagent, amount) in reagents)
        {
            amounts.Add(reagent, FixedPoint2.New(amount.Amount));
        }

        return amounts;
    }

    /// <summary>
    ///     One line for everything the reaction needs: where it runs and its temperatures, then each other condition.
    /// </summary>
    private static List<string> KsFluidConditions(KsFluidReactionPrototype prototype, MixingCategoryPrototype category, IPrototypeManager protoMan)
    {
        var lines = new List<string>
        {
            Loc.GetString("guidebook-reagent-recipes-mix-info",
                ("verb", Loc.GetString(category.VerbText)),
                ("minTemp", prototype.MinTemp),
                ("maxTemp", prototype.MaxTemp),
                ("hasMax", !float.IsPositiveInfinity(prototype.MaxTemp))),
        };

        if (prototype.Catalyst is { } catalyst)
        {
            lines.Add(Loc.GetString("ks-guidebook-fluid-reaction-catalyst",
                ("catalyst", Loc.GetString(protoMan.Index(catalyst).Name))));
        }

        if (prototype.CatalystReagent is { } catalystReagent)
        {
            lines.Add(Loc.GetString("ks-guidebook-fluid-reaction-catalyst-reagent",
                ("units", prototype.CatalystReagentUnits),
                ("reagent", protoMan.Index(catalystReagent).LocalizedName)));
        }

        foreach (var (reagent, pressure) in prototype.MinPartialPressure)
        {
            lines.Add(Loc.GetString("ks-guidebook-fluid-reaction-pressure",
                ("reagent", protoMan.Index(reagent).LocalizedName),
                ("pressure", pressure)));
        }

        return lines;
    }
}
