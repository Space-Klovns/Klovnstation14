#nullable enable
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared._KS14.NPC;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcSquadGoap"/> on its own: what is so and what things cost go in, the cheapest order of steps comes out.
/// </summary>
[TestFixture]
public sealed class KsNpcSquadGoapTest
{
    private const NpcSquadFact Door = NpcSquadFact.HasWayIn | NpcSquadFact.DoorForceable;
    private const NpcSquadFact OpenWay = NpcSquadFact.HasWayIn | NpcSquadFact.WayOpen;

    private static float[] Costs(float stackUp = 1f, float forceDoor = 6f, float enter = 2f, float breakGlass = 9f)
    {
        return [stackUp, forceDoor, enter, breakGlass];
    }

    private static NpcSquadAction[]? Plan(NpcSquadFact facts, float[] costs, params NpcSquadAction[] banned)
    {
        var bans = 0;
        foreach (var action in banned)
        {
            bans |= NpcSquadGoap.GetBan(action);
        }

        var steps = new NpcSquadAction[NpcSquadGoap.MaxSteps];
        return NpcSquadGoap.TryPlan(facts, NpcSquadFact.HasShot, costs, bans, steps, out var length, out _) ? steps[..length] : null;
    }

    /// <summary>
    ///     A way in that is shut takes several steps, each needing the last: the point of having a planner.
    /// </summary>
    [Test]
    public void TestChainsThroughAShutDoor()
    {
        Assert.That(Plan(Door, Costs()), Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.ForceDoor, NpcSquadAction.Enter }));
    }

    /// <summary>
    ///     No door to force, no step for it.
    /// </summary>
    [Test]
    public void TestAnOpenWayIsWalked()
    {
        Assert.That(Plan(OpenWay, Costs()), Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.Enter }));
    }

    /// <summary>
    ///     Whichever is cheaper, from the same facts.
    /// </summary>
    [TestCase(1f, NpcSquadAction.StackUp)]
    [TestCase(50f, NpcSquadAction.BreakGlass)]
    public void TestTakesTheCheapestWay(float stackUp, NpcSquadAction first)
    {
        var plan = Plan(Door | NpcSquadFact.GlassBreakable, Costs(stackUp: stackUp));

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan![0], Is.EqualTo(first));
    }

    /// <summary>
    ///     Having stacked up, it does not do so again.
    /// </summary>
    [Test]
    public void TestDoesNotRepeatWhatIsDone()
    {
        Assert.That(Plan(Door | NpcSquadFact.Stacked, Costs()), Is.EqualTo(new[] { NpcSquadAction.ForceDoor, NpcSquadAction.Enter }));
    }

    /// <summary>
    ///     A shut door with nothing to force it with is no way at all.
    /// </summary>
    [TestCase(NpcSquadFact.None)]
    [TestCase(NpcSquadFact.HasWayIn)]
    public void TestNothingToBeDone(NpcSquadFact facts)
    {
        Assert.That(Plan(facts, Costs()), Is.Null);
    }

    /// <summary>
    ///     What has failed is not tried again, and whatever is left is.
    /// </summary>
    [Test]
    public void TestBannedActionsAreNotUsed()
    {
        var facts = Door | NpcSquadFact.GlassBreakable;

        Assert.That(Plan(facts, Costs(), NpcSquadAction.ForceDoor), Is.EqualTo(new[] { NpcSquadAction.BreakGlass }));
        Assert.That(Plan(facts, Costs(), NpcSquadAction.ForceDoor, NpcSquadAction.BreakGlass), Is.Null);
    }

    [Test]
    public void TestDoesNotAllocate()
    {
        var costs = Costs();
        Span<NpcSquadAction> steps = stackalloc NpcSquadAction[NpcSquadGoap.MaxSteps];

        NpcSquadGoap.TryPlan(Door, NpcSquadFact.HasShot, costs, 0, steps, out _, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
        {
            NpcSquadGoap.TryPlan(Door, NpcSquadFact.HasShot, costs, 0, steps, out _, out _);
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
}
