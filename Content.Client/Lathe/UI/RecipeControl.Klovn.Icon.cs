using Content.Shared.Research.Prototypes;

namespace Content.Client.Lathe.UI;

public sealed partial class RecipeControl
{
    /// <summary>
    ///     Whether this row is already showing the recipe, and so has the right icon.
    /// </summary>
    public bool KsShows(LatheRecipePrototype recipe)
    {
        return _recipeId == recipe.ID;
    }
}
