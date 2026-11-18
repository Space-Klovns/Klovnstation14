using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.Perks;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Perks

    [Dependency] private SharedKsPerkSystem _perkSystem = default!;
    [Dependency] private SharedKsPerkChoiceSystem _perkChoiceSystem = default!;

    /// <summary>
    ///     The perk a defender picked this round, if any.
    /// </summary>
    public bool TryGetChosenPerk(KsFieldCommanderRuleComponent component, NetUserId userId, [NotNullWhen(true)] out ProtoId<KsPerkPrototype>? perk)
    {
        if (component.ChosenPerks.TryGetValue(userId, out var chosenPerk))
        {
            perk = chosenPerk;
            return true;
        }

        perk = null;
        return false;
    }

    /// <summary>
    ///     Gives a defender's body the perk they picked, if they have picked one. Separate from asking
    ///         (<see cref="RequestPerk"/>), so a perk picked some other way (before joining, by an admin) is applied the
    ///         same way.
    /// </summary>
    public bool ApplyStoredPerk(Entity<KsFieldCommanderRuleComponent> rule, EntityUid bodyUid, NetUserId userId)
    {
        return rule.Comp.ChosenPerks.TryGetValue(userId, out var perk) && _perkSystem.TryAddPerk(bodyUid, perk);
    }

    /// <summary>
    ///     Opens the perk picker on a defender's body, offering the rule's perks. The answer comes back in
    ///         <see cref="OnPerkChosen"/>.
    /// </summary>
    public bool RequestPerk(Entity<KsFieldCommanderRuleComponent> rule, EntityUid bodyUid)
    {
        return rule.Comp.PerkOptions.Count > 0 &&
               _perkChoiceSystem.RequestChoice(bodyUid, rule.Comp.PerkOptions, source: rule.Owner);
    }

    /// <summary>
    ///     Records a defender's perk for the round, and swaps it onto the body they are in now: any perk of the rule's
    ///         they held there is taken away, and an open picker is closed.
    /// </summary>
    public void SetChosenPerk(Entity<KsFieldCommanderRuleComponent> rule, ICommonSession session, ProtoId<KsPerkPrototype> perk)
    {
        rule.Comp.ChosenPerks[session.UserId] = perk;

        if (session.AttachedEntity is not { } bodyUid || !IsDefender(rule.Comp, bodyUid))
            return;

        _perkChoiceSystem.CancelChoice(bodyUid);

        if (TryComp<KsPerkHolderComponent>(bodyUid, out var holderComponent))
        {
            foreach (var option in rule.Comp.PerkOptions)
            {
                if (option != perk)
                    _perkSystem.RemovePerk((bodyUid, holderComponent), option);
            }
        }

        _perkSystem.TryAddPerk(bodyUid, perk);
    }

    /// <summary>
    ///     What a defender's new body gets: the perk they picked, or the picker if they haven't.
    /// </summary>
    private void ApplyOrRequestPerk(Entity<KsFieldCommanderRuleComponent> rule, EntityUid bodyUid, ICommonSession session)
    {
        if (rule.Comp.ChosenPerks.ContainsKey(session.UserId))
            ApplyStoredPerk(rule, bodyUid, session.UserId);
        else
            RequestPerk(rule, bodyUid);
    }

    [SubscribeLocalEvent]
    private void OnPerkChosen(ref KsPerkChosenEvent args)
    {
        if (args.Handled ||
            args.Source is not { } ruleUid ||
            !TryComp<KsFieldCommanderRuleComponent>(ruleUid, out var ruleComponent) ||
            !TryComp<ActorComponent>(args.Actor, out var actorComponent))
            return;

        args.Handled = true;
        var rule = new Entity<KsFieldCommanderRuleComponent>(ruleUid, ruleComponent);
        var userId = actorComponent.PlayerSession.UserId;

        // Picked once per round: a second picker (an admin's, a body prepared twice) doesn't change it.
        if (!ruleComponent.ChosenPerks.ContainsKey(userId))
            ruleComponent.ChosenPerks[userId] = args.Perk;

        ApplyStoredPerk(rule, args.Body, userId);
    }

    #endregion
}
