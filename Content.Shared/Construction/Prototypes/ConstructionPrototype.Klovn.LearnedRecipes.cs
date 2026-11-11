using Content.Shared._KS14.LearnedRecipes;

namespace Content.Shared.Construction.Prototypes;

public sealed partial class ConstructionPrototype
{
    /// <summary>
    ///     The recipe stays hidden and cannot be started until the user's mind learns it.
    ///         tgstation's <c>CRAFT_MUST_BE_LEARNED</c>.
    /// </summary>
    /// <seealso cref="KsLearnedRecipesComponent"/>
    [DataField]
    public bool KsMustBeLearned { get; private set; }
}
