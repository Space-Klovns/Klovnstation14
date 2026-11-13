using Content.Shared.Construction.Prototypes;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.LearnedRecipes;

/// <summary>
///     A book that teaches crafting recipes to whoever reads it through. tgstation's
///         <c>/obj/item/book/granter/crafting_recipe</c>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsRecipeGranterSystem))]
public sealed partial class KsRecipeGranterComponent : Component
{
    [DataField(required: true)]
    public List<ProtoId<ConstructionPrototype>> Recipes = new();

    /// <summary>
    ///     How many pages are turned, each taking <see cref="ReadTime"/>, before the final read.
    /// </summary>
    [DataField]
    public int PagesToMastery = 3;

    [DataField]
    public TimeSpan ReadTime = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How many times it can be read through. Reading it after that crumbles it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Uses = 1;

    /// <summary>
    ///     One is shown at random after each page.
    /// </summary>
    [DataField]
    public List<LocId> Remarks = new();

    [DataField]
    public SoundSpecifier PageTurnSound = new SoundCollectionSpecifier("KsPageTurn", AudioParams.Default.WithVariation(0.1f).WithVolume(-4f));

    /// <summary>
    ///     Shown when it is read with no uses left, right before it is deleted.
    /// </summary>
    [DataField]
    public LocId RecoilMessage = "ks-recipe-granter-recoil";
}

[Serializable, NetSerializable]
public sealed partial class KsRecipeGranterDoAfterEvent : DoAfterEvent
{
    /// <summary>
    ///     The page that was being read. Pages past <see cref="KsRecipeGranterComponent.PagesToMastery"/> are the
    ///         final read.
    /// </summary>
    public int Page;

    public KsRecipeGranterDoAfterEvent(int page)
    {
        Page = page;
    }

    public override DoAfterEvent Clone() => this;
}
