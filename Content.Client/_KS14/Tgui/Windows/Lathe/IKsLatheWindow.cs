using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.Lathe;

/// <summary>
///     What the lathe's bound user interface needs of its window, so that the stock menu and the tgui one are interchangeable.
/// </summary>
public interface IKsLatheWindow
{
    event Action<BaseButton.ButtonEventArgs>? OnServerListButtonPressed;
    event Action<string, int>? RecipeQueueAction;

    /// <summary>
    ///     Ask the salvagers for what the recipe is short of.
    /// </summary>
    event Action<string>? KsRecipeRequestAction;
    event Action<int>? QueueDeleteAction;
    event Action<int>? QueueMoveUpAction;
    event Action<int>? QueueMoveDownAction;
    event Action? DeleteFabricatingAction;

    void SetEntity(EntityUid uid);

    void SetRecipes(List<ProtoId<LatheRecipePrototype>> recipes);

    void PopulateRecipes();

    void UpdateCategories();

    void PopulateQueueList(IReadOnlyCollection<LatheRecipeBatch> queue);

    void SetQueueInfo(ProtoId<LatheRecipePrototype>? recipeProto);
}
