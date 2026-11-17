using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Client.Lathe.UI;

public sealed partial class QueuedRecipeControl
{
    private ProtoId<LatheRecipePrototype>? _ksRecipe;

    /// <summary>
    ///     Notes the recipe this row shows. False means it already showed it, and has the right icon.
    /// </summary>
    public bool KsTakeRecipe(ProtoId<LatheRecipePrototype> recipe)
    {
        if (_ksRecipe == recipe)
            return false;

        _ksRecipe = recipe;
        return true;
    }
}
