using Content.Shared.Construction.Prototypes;
using Content.Shared.Mind;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.LearnedRecipes;

public sealed partial class KsLearnedRecipesSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;

    /// <summary>
    ///     Raised on the client whenever the learned recipes of any mind change, so the crafting menu can refresh.
    /// </summary>
    public event Action? RecipesChanged;

    [SubscribeLocalEvent]
    private void OnAfterHandleState(Entity<KsLearnedRecipesComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        RecipesChanged?.Invoke();
    }

    /// <summary>
    ///     Server only: the mind's knowledge reaches its owner through state, never by prediction.
    /// </summary>
    public void Teach(EntityUid mindId, ProtoId<ConstructionPrototype> recipe)
    {
        if (_netManager.IsClient)
            return;

        var learned = EnsureComp<KsLearnedRecipesComponent>(mindId);
        if (!learned.Recipes.Add(recipe))
            return;

        Dirty(mindId, learned);
        RecipesChanged?.Invoke();
    }

    public void Forget(EntityUid mindId, ProtoId<ConstructionPrototype> recipe)
    {
        if (_netManager.IsClient ||
            !TryComp<KsLearnedRecipesComponent>(mindId, out var learned) ||
            !learned.Recipes.Remove(recipe))
            return;

        Dirty(mindId, learned);
        RecipesChanged?.Invoke();
    }

    /// <summary>
    ///     Whether <paramref name="user"/> may see and start <paramref name="recipe"/>. Recipes that don't need to be
    ///         learned are always allowed.
    /// </summary>
    public bool CanUse(EntityUid user, ConstructionPrototype recipe)
    {
        return !recipe.KsMustBeLearned || Knows(user, recipe.ID);
    }

    public bool Knows(EntityUid user, ProtoId<ConstructionPrototype> recipe)
    {
        return _mindSystem.TryGetMind(user, out var mindId, out _) &&
            TryComp<KsLearnedRecipesComponent>(mindId, out var learned) &&
            learned.Recipes.Contains(recipe);
    }
}
