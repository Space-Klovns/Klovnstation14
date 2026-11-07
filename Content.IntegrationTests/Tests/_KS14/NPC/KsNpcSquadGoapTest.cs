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
    private const NpcSquadFact Ready = NpcSquadFact.Patched | NpcSquadFact.Loaded;
    private const NpcSquadFact Door = NpcSquadFact.HasWayIn | NpcSquadFact.DoorForceable | Ready;
    private const NpcSquadFact OpenWay = NpcSquadFact.HasWayIn | NpcSquadFact.WayOpen | Ready;

    private static float[] Costs(float stackUp = 1f, float forceDoor = 6f, float enter = 2f, float breakGlass = 9f, float hold = 13f)
    {
        return [stackUp, forceDoor, enter, breakGlass, 2f, 2f, 4f, 3f, hold, 3f, 1.5f, 8f, 10f, 3.5f];
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
    ///     The same room, and what is in it decides: walking in on a gun costs more than a flashbang does, and walking in on
    ///         nobody much does not.
    /// </summary>
    [Test]
    public void TestAFlashbangGoesOnlyWhereItIsWorthIt()
    {
        var facts = Door | NpcSquadFact.HasFlashbang;

        Assert.That(Plan(facts, Costs(enter: 8f)), Is.EqualTo(new[]
        {
            NpcSquadAction.StackUp, NpcSquadAction.ForceDoor, NpcSquadAction.Flashbang, NpcSquadAction.EnterSoftened,
        }));
        Assert.That(Plan(facts, Costs(enter: 2f)), Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.ForceDoor, NpcSquadAction.Enter }));
    }

    /// <summary>
    ///     What a squad does about a gun behind a shut door, cheapest first, as the costs in the game have it: softening it up
    ///         with what it has, then waiting for it to come out, and only then forcing the door and walking in on it.
    /// </summary>
    [TestCase(NpcSquadFact.HasFlashbang | NpcSquadFact.ExitsCovered, NpcSquadAction.Flashbang)]
    [TestCase(NpcSquadFact.HasFrag | NpcSquadFact.ExitsCovered, NpcSquadAction.FragRoom)]
    [TestCase(NpcSquadFact.HasFlashbang | NpcSquadFact.HasFrag, NpcSquadAction.Flashbang)]
    [TestCase(NpcSquadFact.ExitsCovered, NpcSquadAction.HoldExits)]
    [TestCase(NpcSquadFact.HasSmoke | NpcSquadFact.ExitsCovered, NpcSquadAction.SmokeRoom)]
    [TestCase(NpcSquadFact.HasFlashbang | NpcSquadFact.HasSmoke, NpcSquadAction.Flashbang)]
    [TestCase(NpcSquadFact.HasFrag | NpcSquadFact.HasSmoke, NpcSquadAction.FragRoom)]
    public void TestAGunBehindAShutDoor(NpcSquadFact has, NpcSquadAction expected)
    {
        var plan = Plan(Door | has, Costs(enter: 8f));

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan, Does.Contain(expected));
    }

    /// <summary>
    ///     The same with the way standing open, where waiting costs less as there is less to get through.
    /// </summary>
    [TestCase(NpcSquadFact.HasFlashbang | NpcSquadFact.ThrowLine | NpcSquadFact.ExitsCovered, NpcSquadAction.Flashbang)]
    [TestCase(NpcSquadFact.HasFrag | NpcSquadFact.ThrowLine | NpcSquadFact.ExitsCovered, NpcSquadAction.FragRoom)]
    [TestCase(NpcSquadFact.HasSmoke | NpcSquadFact.ThrowLine | NpcSquadFact.ExitsCovered, NpcSquadAction.SmokeRoom)]
    [TestCase(NpcSquadFact.ExitsCovered, NpcSquadAction.HoldExits)]
    public void TestAGunBehindAnOpenWay(NpcSquadFact has, NpcSquadAction expected)
    {
        Assert.That(Plan(OpenWay | has, Costs(enter: 8f, hold: 6f)), Does.Contain(expected));
    }

    /// <summary>
    ///     Nobody waits for what can just be walked in on.
    /// </summary>
    [Test]
    public void TestNobodyWaitsOutNobodyMuch()
    {
        Assert.That(Plan(OpenWay | NpcSquadFact.ExitsCovered, Costs(hold: 6f)), Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.Enter }));
    }

    /// <summary>
    ///     Nobody goes in hurt or low with what would fix that to hand, and both are seen to before anything opens the door.
    /// </summary>
    [Test]
    public void TestSeesToItselfBeforeOpeningTheDoor()
    {
        var facts = NpcSquadFact.HasWayIn | NpcSquadFact.DoorForceable;

        Assert.That(Plan(facts, Costs()), Is.EqualTo(new[]
        {
            NpcSquadAction.StackUp, NpcSquadAction.PatchUp, NpcSquadAction.TopUp, NpcSquadAction.ForceDoor, NpcSquadAction.Enter,
        }));
    }

    /// <summary>
    ///     Only going in calls for that.
    /// </summary>
    [Test]
    public void TestShootingOutGlassDoesNotWaitForThat()
    {
        Assert.That(Plan(NpcSquadFact.GlassBreakable, Costs()), Is.EqualTo(new[] { NpcSquadAction.BreakGlass }));
    }

    /// <summary>
    ///     A door nobody can open is no end of it, with a charge.
    /// </summary>
    [Test]
    public void TestBlowsADoorNothingElseOpens()
    {
        var facts = NpcSquadFact.HasWayIn | NpcSquadFact.HasCharge | NpcSquadFact.DoorChargeable | Ready;

        Assert.That(Plan(facts, Costs()), Is.EqualTo(new[] { NpcSquadAction.StackUp, NpcSquadAction.BlowDoor, NpcSquadAction.Enter }));
    }

    /// <summary>
    ///     A tool is cheaper than a charge, for as long as it works.
    /// </summary>
    [Test]
    public void TestAToolComesBeforeACharge()
    {
        var facts = Door | NpcSquadFact.HasCharge | NpcSquadFact.DoorChargeable;

        Assert.That(Plan(facts, Costs()), Does.Contain(NpcSquadAction.ForceDoor));
        Assert.That(Plan(facts, Costs(), NpcSquadAction.ForceDoor), Does.Contain(NpcSquadAction.BlowDoor));
    }

    /// <summary>
    ///     No way in at all, and one gets made: nothing has to be stacked up on, as that is where the blast sends them.
    /// </summary>
    [Test]
    public void TestAWallMakesAWayIn()
    {
        var facts = NpcSquadFact.HasCharge | NpcSquadFact.WallChargeable | Ready;

        Assert.That(Plan(facts, Costs()), Is.EqualTo(new[] { NpcSquadAction.BlowWall, NpcSquadAction.Enter }));
    }

    /// <summary>
    ///     Every step a plan could want at once still fits in a plan.
    /// </summary>
    [Test]
    public void TestTheLongestPlanFits()
    {
        var facts = NpcSquadFact.HasWayIn | NpcSquadFact.HasCharge | NpcSquadFact.DoorChargeable | NpcSquadFact.HasFlashbang;

        Assert.That(Plan(facts, Costs(enter: 20f)), Is.EqualTo(new[]
        {
            NpcSquadAction.StackUp, NpcSquadAction.PatchUp, NpcSquadAction.TopUp, NpcSquadAction.BlowDoor, NpcSquadAction.Flashbang, NpcSquadAction.EnterSoftened,
        }));
    }

    /// <summary>
    ///     Getting out of sight takes being in danger of being hit, and is its own goal.
    /// </summary>
    [TestCase(NpcSquadFact.Threatened, true)]
    [TestCase(NpcSquadFact.None, false)]
    public void TestTakingCoverTakesAReason(NpcSquadFact facts, bool plans)
    {
        var steps = new NpcSquadAction[NpcSquadGoap.MaxSteps];
        Assert.That(NpcSquadGoap.TryPlan(facts, NpcSquadFact.OutOfSight, Costs(), 0, steps, out var length, out _), Is.EqualTo(plans));

        if (plans)
            Assert.That(steps[..length], Is.EqualTo(new[] { NpcSquadAction.TakeCover }));
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

    /// <summary>
    ///     With everything possible at once and nothing reaching the goal, it has to look at every order of things. It is asked
    ///         this in the middle of a tick, so a new action, or a longer plan, should not be allowed to make that matter.
    /// </summary>
    [Test]
    public void TestTheWorstCaseIsQuick()
    {
        var everything = (NpcSquadFact) ((1u << 18) - 1) & ~NpcSquadFact.HasShot & ~NpcSquadFact.OutOfSight;
        var facts = everything & ~(NpcSquadFact.Stacked | NpcSquadFact.Softened | NpcSquadFact.Patched | NpcSquadFact.Loaded);
        var costs = Costs();
        var bans = NpcSquadGoap.GetBan(NpcSquadAction.Enter) | NpcSquadGoap.GetBan(NpcSquadAction.EnterSoftened) |
            NpcSquadGoap.GetBan(NpcSquadAction.HoldExits) | NpcSquadGoap.GetBan(NpcSquadAction.BreakGlass);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 100; i++)
        {
            var steps = new NpcSquadAction[NpcSquadGoap.MaxSteps];
            Assert.That(NpcSquadGoap.TryPlan(facts, NpcSquadFact.HasShot, costs, bans, steps, out _, out _), Is.False);
        }

        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(200), "100 plans should take no time at all");
    }
}
