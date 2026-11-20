using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Construction.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.LearnedRecipes;

public sealed partial class KsRecipeGranterSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;
    [Dependency] private KsLearnedRecipesSystem _learnedRecipesSystem = default!;

    [SubscribeLocalEvent]
    private void OnUseInHand(Entity<KsRecipeGranterComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (TryComp<BlindableComponent>(args.User, out var blindableComponent) && blindableComponent.IsBlind)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-recipe-granter-blind"), args.User, args.User);
            return;
        }

        if (!_mindSystem.TryGetMind(args.User, out _, out _))
            return;

        if (entity.Comp.Uses <= 0)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(entity.Comp.RecoilMessage), args.User, args.User, PopupType.SmallCaution);
            PredictedQueueDel(entity.Owner);
            return;
        }

        if (!StartPage(entity, args.User, page: 1))
            return;

        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-recipe-granter-start", ("book", entity.Owner)), args.User, args.User);
    }

    [SubscribeLocalEvent]
    private void OnDoAfter(Entity<KsRecipeGranterComponent> entity, ref KsRecipeGranterDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-recipe-granter-stop"), args.User, args.User);
            return;
        }

        if (args.Handled)
            return;

        args.Handled = true;

        // A page was turned: say something about it, then read on.
        if (args.Page <= entity.Comp.PagesToMastery)
        {
            _popupSystem.PopupEntity(PickRemark(entity, args.User), args.User, args.User);

            StartPage(entity, args.User, args.Page + 1);
            return;
        }

        // The final read.
        entity.Comp.Uses--;
        Dirty(entity);

        if (!_mindSystem.TryGetMind(args.User, out var mindId, out _))
            return;

        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-recipe-granter-finish", ("book", entity.Owner)), args.User, args.User);

        foreach (var recipeId in entity.Comp.Recipes)
        {
            _learnedRecipesSystem.Teach(mindId, recipeId);

            if (ProtoMan.Resolve(recipeId, out var recipe))
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-recipe-granter-learned", ("recipe", GetRecipeName(recipe))), args.User, args.User);
        }
    }

    /// <summary>
    ///     tgstation's <c>turn_page</c>: the page turns, then it takes <see cref="KsRecipeGranterComponent.ReadTime"/>
    ///         to read. Pages past <see cref="KsRecipeGranterComponent.PagesToMastery"/> are the final read, which
    ///         turns no page.
    /// </summary>
    private bool StartPage(Entity<KsRecipeGranterComponent> entity, EntityUid user, int page)
    {
        var doAfterArgs = new DoAfterArgs(EntityManager, user, entity.Comp.ReadTime, new KsRecipeGranterDoAfterEvent(page), entity.Owner, used: entity.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnHandChange = true,
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return false;

        if (page <= entity.Comp.PagesToMastery)
            _audioSystem.PlayPredicted(entity.Comp.PageTurnSound, user, user);

        return true;
    }

    private KsPopupMessage PickRemark(Entity<KsRecipeGranterComponent> entity, EntityUid user)
    {
        if (entity.Comp.Remarks.Count == 0)
            return KsPopupMessage.Create("ks-recipe-granter-keep-reading");

        // Seeded from the tick so the predicting client picks the same remark as the server.
        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            KsSharedRandomExtensions.GetNetId(entity.Owner, EntityManager),
            KsSharedRandomExtensions.GetNetId(user, EntityManager),
            (int)_gameTiming.CurTick.Value);

        return KsPopupMessage.Create(entity.Comp.Remarks[predictedRandom.Next(entity.Comp.Remarks.Count)]);
    }

    private object GetRecipeName(ConstructionPrototype recipe)
    {
        if (recipe.SetName is { } setName)
            return KsPopupMessage.Create(setName);

        if (ProtoMan.Resolve(recipe.Graph, out var graph) &&
            graph.Nodes.TryGetValue(recipe.TargetNode, out var targetNode) &&
            targetNode.Entity.GetId(null, null, new(EntityManager)) is { } entityId)
            return new KsPopupPrototypeName(entityId);

        return recipe.ID;
    }
}
