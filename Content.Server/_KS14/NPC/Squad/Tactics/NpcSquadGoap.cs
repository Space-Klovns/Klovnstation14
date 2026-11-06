using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     What an action needs to be so, and what it makes so.
/// </summary>
public readonly record struct NpcSquadActionDefinition(NpcSquadAction Action, NpcSquadFact Requires, NpcSquadFact Adds);

/// <summary>
///     Works out the cheapest order of actions that gets a squad from what is so to what it wants. Knows nothing of the game:
///         what is so and what things cost come in from <see cref="NpcSquadTacticsSystem"/>, which carries the answer out.
/// </summary>
/// <remarks>
///     A plain depth-first search that gives up on anything dearer than the best so far. A handful of actions and steps,
///         asked a few times a plan, so there is nothing to gain from an open list. Allocates nothing.
/// </remarks>
public static class NpcSquadGoap
{
    /// <summary>
    ///     The most steps a plan can have. Anything longer will have gone stale before it is done.
    /// </summary>
    public const int MaxSteps = 4;

    public static readonly NpcSquadActionDefinition[] Actions =
    [
        new(NpcSquadAction.StackUp, NpcSquadFact.HasWayIn, NpcSquadFact.Stacked),
        new(NpcSquadAction.ForceDoor, NpcSquadFact.Stacked | NpcSquadFact.DoorForceable, NpcSquadFact.WayOpen),
        new(NpcSquadAction.Enter, NpcSquadFact.Stacked | NpcSquadFact.WayOpen, NpcSquadFact.HasShot),
        new(NpcSquadAction.BreakGlass, NpcSquadFact.GlassBreakable, NpcSquadFact.HasShot),
    ];

    /// <summary>
    ///     The bit of <paramref name="action"/> in the banned actions passed to <see cref="TryPlan"/>.
    /// </summary>
    public static int GetBan(NpcSquadAction action)
    {
        return 1 << (int) action;
    }

    /// <summary>
    ///     The cheapest way from <paramref name="facts"/> to <paramref name="goal"/>, if there is one.
    /// </summary>
    /// <param name="costs">What each action costs, by its value in <see cref="NpcSquadAction"/>.</param>
    /// <param name="bannedActions">Actions not to use, as bits from <see cref="GetBan"/>.</param>
    /// <param name="plan">Where the steps go. Needs room for <see cref="MaxSteps"/>.</param>
    public static bool TryPlan(NpcSquadFact facts,
        NpcSquadFact goal,
        ReadOnlySpan<float> costs,
        int bannedActions,
        Span<NpcSquadAction> plan,
        out int length,
        out float cost)
    {
        Span<NpcSquadAction> steps = stackalloc NpcSquadAction[MaxSteps];

        length = -1;
        cost = float.MaxValue;
        Search(facts, goal, costs, bannedActions, steps, 0, 0f, plan, ref length, ref cost);

        return length >= 0;
    }

    private static void Search(NpcSquadFact facts,
        NpcSquadFact goal,
        ReadOnlySpan<float> costs,
        int bannedActions,
        Span<NpcSquadAction> steps,
        int depth,
        float costSoFar,
        Span<NpcSquadAction> best,
        ref int bestLength,
        ref float bestCost)
    {
        if ((facts & goal) == goal)
        {
            steps[..depth].CopyTo(best);
            bestLength = depth;
            bestCost = costSoFar;
            return;
        }

        if (depth == MaxSteps)
            return;

        foreach (var definition in Actions)
        {
            if ((bannedActions & GetBan(definition.Action)) != 0x0 ||
                (facts & definition.Requires) != definition.Requires)
                continue;

            // Getting nowhere: this is what keeps the search from going round in circles.
            var next = facts | definition.Adds;
            if (next == facts)
                continue;

            var cost = costSoFar + costs[(int) definition.Action];
            if (cost >= bestCost)
                continue;

            steps[depth] = definition.Action;
            Search(next, goal, costs, bannedActions, steps, depth + 1, cost, best, ref bestLength, ref bestCost);
        }
    }
}
