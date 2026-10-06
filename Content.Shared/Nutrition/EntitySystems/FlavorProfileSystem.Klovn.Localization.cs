using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Chemistry.Components;
using Content.Shared.Nutrition.Components;

namespace Content.Shared.Nutrition.EntitySystems;

public sealed partial class FlavorProfileSystem
{
    public KsPopupMessage GetFlavorsPopupMessage(Entity<FlavorProfileComponent?> entity, EntityUid user, Solution? solution)
    {
        HashSet<string> flavors = new();
        HashSet<string>? ignore = null;

        if (Resolve(entity, ref entity.Comp, false))
        {
            flavors = entity.Comp.Flavors;
            ignore = entity.Comp.IgnoreReagents;
        }


        if (solution != null)
            flavors.UnionWith(GetFlavorsFromReagents(solution, FlavorLimit - flavors.Count, ignore));

        var ev = new FlavorProfileModificationEvent(user, flavors);

        RaiseLocalEvent(ev);
        RaiseLocalEvent(entity, ev);
        RaiseLocalEvent(user, ev);

        if (flavors.Count == 0)
            return KsPopupMessage.Create(BackupFlavorMessage);

        return FlavorsToPopupMessage(flavors);
    }

    private KsPopupMessage FlavorsToPopupMessage(HashSet<string> flavorSet)
    {
        var flavors = new List<FlavorPrototype>();
        foreach (var flavor in flavorSet)
        {
            if (string.IsNullOrEmpty(flavor) || !ProtoMan.TryIndex<FlavorPrototype>(flavor, out var flavorPrototype))
            {
                continue;
            }

            flavors.Add(flavorPrototype);
        }

        flavors.Sort((a, b) => a.FlavorType.CompareTo(b.FlavorType));

        if (flavors.Count == 1 && !string.IsNullOrEmpty(flavors[0].FlavorDescription))
        {
            return KsPopupMessage.Create("flavor-profile", ("flavor", KsPopupMessage.Create(flavors[0].FlavorDescription)));
        }

        if (flavors.Count > 1)
        {
            var lastFlavor = KsPopupMessage.Create(flavors[^1].FlavorDescription);
            var labels = new KsPopupMessage[flavors.Count - 1];
            for (var index = 0; index < labels.Length; index++)
                labels[index] = KsPopupMessage.Create(flavors[index].FlavorDescription);
            var allFlavors = new KsPopupMessageList(", ", labels);
            return KsPopupMessage.Create("flavor-profile-multiple", ("flavors", allFlavors), ("lastFlavor", lastFlavor));
        }

        return KsPopupMessage.Create(BackupFlavorMessage);
    }

}
