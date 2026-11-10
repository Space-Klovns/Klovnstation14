using System.Linq;
using Content.Shared.CCVar;
using Content.Shared.Chemistry.Components;
using Content.Shared.Nutrition.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared.Nutrition.EntitySystems;

/// <summary>
///     Deals with flavor profiles when you eat something.
/// </summary>
public sealed partial class FlavorProfileSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configManager = default!;

    private const string BackupFlavorMessage = "flavor-profile-unknown";

    private int FlavorLimit => _configManager.GetCVar(CCVars.FlavorLimit);

    public string GetLocalizedFlavorsMessage(Entity<FlavorProfileComponent?> entity, EntityUid user, Solution? solution)
    {
        return GetFlavorsPopupMessage(entity, user, solution).Format(EntityManager); // KS14: retain string API for immediate formatting
    }

    public string GetLocalizedFlavorsMessage(EntityUid user, Solution solution)
    {
        var flavors = GetFlavorsFromReagents(solution, FlavorLimit);
        var ev = new FlavorProfileModificationEvent(user, flavors);
        RaiseLocalEvent(user, ev, true);

        return FlavorsToPopupMessage(flavors).Format(EntityManager); // KS14: reuse deferred flavour formatting
    }

    private HashSet<string> GetFlavorsFromReagents(Solution solution, int desiredAmount, HashSet<string>? toIgnore = null)
    {
        var flavors = new HashSet<string>();
        foreach (var (reagent, quantity) in solution.GetReagentPrototypes(ProtoMan))
        {
            if (toIgnore != null && toIgnore.Contains(reagent.ID))
            {
                continue;
            }

            if (flavors.Count == desiredAmount)
            {
                break;
            }

            // don't care if the quantity is negligible
            if (quantity < reagent.FlavorMinimum)
            {
                continue;
            }

            if (reagent.Flavor != null)
                flavors.Add(reagent.Flavor);
        }

        return flavors;
    }
}

public sealed class FlavorProfileModificationEvent : EntityEventArgs
{
    public FlavorProfileModificationEvent(EntityUid user, HashSet<string> flavors)
    {
        User = user;
        Flavors = flavors;
    }

    public EntityUid User { get; }
    public HashSet<string> Flavors { get; }
}
