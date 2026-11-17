using Content.Client._KS14.Tgui.Windows.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Client.Lathe.UI;

// The stock menu and the tgui window share the interface the bound user interface drives them through.
public sealed partial class LatheMenu : IKsLatheWindow
{
    private ProtoId<LatheRecipePrototype>? _ksFabricatingRecipe;

    public void SetRecipes(List<ProtoId<LatheRecipePrototype>> recipes)
    {
        Recipes = recipes;
    }
}
