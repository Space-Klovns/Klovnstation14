using Robust.Shared.Player;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Ghosts

    /// <summary>
    ///     Carries a defender's points over to whatever they are put in that isn't on another side, a ghost above all,
    ///         so that their HUD goes on showing their requisition and the wave timer while they are dead.
    /// </summary>
    // PlayerAttachedEvent is a by-value class event; this is the broadcast copy.
    [SubscribeLocalEvent]
    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        var userId = args.Player.UserId;

        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var ruleComponent, out _))
        {
            if (!ruleComponent.Defenders.Contains(userId) ||
                _scenarioFactionMemberQuery.TryComp(args.Entity, out var factionMemberComponent) &&
                factionMemberComponent.Id != ruleComponent.DefenderFaction)
                continue;

            MirrorPoints(ruleComponent, args.Entity, GetPoints(ruleComponent, userId));
        }
    }

    #endregion
}
