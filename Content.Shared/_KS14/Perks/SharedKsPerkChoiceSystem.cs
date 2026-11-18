using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks;

/// <summary>
///     Asks a player to pick a perk with a radial picker on their own body, which can't be closed until they do.
/// </summary>
/// <remarks>
///     Asking (<see cref="RequestChoice"/>) is separate from storing and applying the answer: the asker hears of it in
///         <see cref="KsPerkChosenEvent"/>, and decides what to do with it.
/// </remarks>
public abstract partial class SharedKsPerkChoiceSystem : EntitySystem
{
    /// <summary>
    ///     The client BUI class, found by name.
    /// </summary>
    public const string BoundUserInterfaceType = "KsPerkChoiceBoundUserInterface";

    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedUserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private SharedKsPerkSystem _perkSystem = default!;
    [Dependency] private EntityQuery<KsPerkChoiceComponent> _choiceQuery = default!;

    /// <summary>
    ///     Whether the body's player still has a perk to pick.
    /// </summary>
    public bool IsChoosing(EntityUid bodyUid)
    {
        return _choiceQuery.HasComp(bodyUid);
    }

    /// <summary>
    ///     Opens the perk picker on the body, offering <paramref name="options"/>, and keeps it open until a valid one is
    ///         picked. Unknown perks are left out. False if none are left, or this is the client.
    /// </summary>
    /// <param name="source">What is asking, passed on in <see cref="KsPerkChosenEvent"/>.</param>
    public bool RequestChoice(EntityUid bodyUid, IEnumerable<ProtoId<KsPerkPrototype>> options, EntityUid? source = null)
    {
        if (_netManager.IsClient || TerminatingOrDeleted(bodyUid))
            return false;

        var validOptions = new List<ProtoId<KsPerkPrototype>>();
        foreach (var option in options)
        {
            if (ProtoMan.HasIndex(option) && !validOptions.Contains(option))
                validOptions.Add(option);
        }

        if (validOptions.Count == 0)
            return false;

        var choiceComponent = EnsureComp<KsPerkChoiceComponent>(bodyUid);
        choiceComponent.Options = validOptions;
        choiceComponent.Source = source;
        Dirty(bodyUid, choiceComponent);

        // No range or interaction checks: it is the player's own body, and a stunned or downed player still has to pick.
        if (!_userInterfaceSystem.HasUi(bodyUid, KsPerkChoiceUiKey.Key))
        {
            _userInterfaceSystem.SetUi(bodyUid, KsPerkChoiceUiKey.Key,
                new InterfaceData(BoundUserInterfaceType, interactionRange: 0f, requireInputValidation: false));
        }

        _userInterfaceSystem.OpenUi(bodyUid, KsPerkChoiceUiKey.Key, bodyUid);
        return true;
    }

    /// <summary>
    ///     Stops asking, and closes the picker.
    /// </summary>
    public void CancelChoice(EntityUid bodyUid)
    {
        if (_netManager.IsClient || !_choiceQuery.HasComp(bodyUid))
            return;

        // Removed first, so closing the picker doesn't open it again.
        RemComp<KsPerkChoiceComponent>(bodyUid);
        _userInterfaceSystem.CloseUi(bodyUid, KsPerkChoiceUiKey.Key);
    }

    [SubscribeLocalEvent]
    private void OnSelect(Entity<KsPerkChoiceComponent> entity, ref KsPerkChoiceSelectMessage args)
    {
        // Sent unpredicted, so only the server hears it; and only from the body's own player.
        if (_netManager.IsClient || args.Actor != entity.Owner)
            return;

        if (!entity.Comp.Options.Contains(args.Perk) || !ProtoMan.HasIndex(args.Perk))
        {
            Log.Warning($"{ToPrettyString(args.Actor)} picked perk {args.Perk}, which they were not offered");
            return;
        }

        var ev = new KsPerkChosenEvent(entity.Owner, args.Actor, args.Perk, entity.Comp.Source);
        CancelChoice(entity.Owner);
        RaiseLocalEvent(entity.Owner, ref ev, broadcast: true);

        if (!ev.Handled)
            _perkSystem.TryAddPerk(entity.Owner, args.Perk);
    }

    // BoundUIClosedEvent is a by-value class event, so the handler takes the uid and component apart.
    [SubscribeLocalEvent]
    private void OnClosed(EntityUid uid, KsPerkChoiceComponent component, BoundUIClosedEvent args)
    {
        if (_netManager.IsClient ||
            !Equals(args.UiKey, KsPerkChoiceUiKey.Key) ||
            component.LifeStage > ComponentLifeStage.Running ||
            TerminatingOrDeleted(uid) ||
            TerminatingOrDeleted(args.Actor))
            return;

        // Still undecided: there is no closing it.
        _userInterfaceSystem.OpenUi(uid, KsPerkChoiceUiKey.Key, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnAfterState(Entity<KsPerkChoiceComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        OnOptionsChanged(entity);
    }

    /// <summary>
    ///     The offered perks arrived or changed; the client refreshes an open picker.
    /// </summary>
    protected virtual void OnOptionsChanged(Entity<KsPerkChoiceComponent> entity)
    {
    }
}
