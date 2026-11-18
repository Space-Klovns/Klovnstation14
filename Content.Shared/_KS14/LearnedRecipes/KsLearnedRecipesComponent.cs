using Content.Shared.Construction.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.LearnedRecipes;

/// <summary>
///     Recipes this mind has learned. Lives on the mind entity, like tgstation's <c>mind.learned_recipes</c>, so
///         the knowledge follows the player between bodies.
/// </summary>
/// <seealso cref="ConstructionPrototype.KsMustBeLearned"/>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class KsLearnedRecipesComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<ConstructionPrototype>> Recipes = new();
}
