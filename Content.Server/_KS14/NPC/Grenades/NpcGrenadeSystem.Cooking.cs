using System.Numerics;
using Content.Server._KS14.NPC.LineOfFire;
using Content.Server._KS14.NPC.Voices;
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     What came of asking an NPC to throw a grenade (<see cref="NpcGrenadeSystem.TryBeginThrow"/>).
/// </summary>
public enum NpcGrenadeThrowResult : byte
{
    /// <summary>
    ///     Nothing was thrown, and the grenade is unlit and back where it was.
    /// </summary>
    Failed,

    /// <summary>
    ///     It left the hand.
    /// </summary>
    Thrown,

    /// <summary>
    ///     It is lit and in the hand, and will be thrown by the system when its time comes, or before.
    /// </summary>
    Cooking,
}

/// <summary>
///     Cooking: a thrower with <see cref="NpcGrenadeThrowerComponent.Cooks"/> lights a frag (an explosive with a timer fuse),
///         holds it, and throws it when only <see cref="NpcGrenadeThrowerComponent.CookMargin"/> of the fuse is left once it
///         has landed, so there is that much less time for anyone to get clear.
/// </summary>
/// <remarks>
///     <para>
///         <b>The grenade never goes off in hand.</b> A lit grenade in a hand is the one thing the all-at-once throw was
///             written to avoid, so a cook is held by this system and not by the HTN task that began it, and ends by the
///             grenade being thrown, at once, wherever it was meant for:
///     </para>
///     <list type="bullet">
///         <item>the time is up (<see cref="NpcGrenadeCookComponent.ReleaseAt"/>, which is never later than the fuse less the
///             flight less <see cref="MinCookMargin"/>, whatever the settings say);</item>
///         <item>the task that began it ends any way at all: <c>ThrowGrenadeOperator.TaskShutdown</c> calls
///             <see cref="EndCook"/>, which a replan, a better plan or a failed task all reach;</item>
///         <item>the task stops saying it is cooking (<see cref="NpcGrenadeCookComponent.LeaseUntil"/>), which is how a death,
///             a sleeping NPC or a removed HTN is noticed, none of which end a task;</item>
///         <item>the NPC is dead, or can no longer throw (a stun).</item>
///     </list>
///     <para>
///         When the throw cannot be made (a stunned or dead thrower cannot throw) the grenade is dropped where it stands. It is
///             then an ordinary lit grenade on the floor, which everyone able to move runs from
///             (<c>grenade.yml</c>, and the Blast tile danger in <c>PathfindingSystem.Klovn.Blast.cs</c>, which counts it the
///             moment it is out of the hand); a thrower that cannot move cannot run from it, which no design can help.
///             Anything that is not an explosive, or not on a timer, is thrown at once as it always was.
///     </para>
///     <para>
///         <b>The squad.</b> The grenade is in a container while it is held, so the blast tile dangers leave it out.
///             <see cref="TryGetLanding"/> answers with where it is to land from the moment the hold starts, so squadmates
///             start moving early rather than late. The voice callout (the "EFD" call) is made as it is thrown, as it is
///             heard: not when it is dropped.
///             Once it is thrown it is a thrown grenade like any other, with the Blast danger round its landing spot.
///     </para>
/// </remarks>
public sealed partial class NpcGrenadeSystem
{
    /// <summary>
    ///     Of fuse, in seconds, that is always left on landing, whatever <see cref="NpcGrenadeThrowerComponent.CookMargin"/> and
    ///         its spread say.
    /// </summary>
    public const float MinCookMargin = 0.35f;

    /// <summary>
    ///     A hold shorter than this, in seconds, is not worth the trouble: the grenade is just thrown.
    /// </summary>
    private const float MinCookTime = 0.25f;

    /// <summary>
    ///     How long a task may go without renewing its hold (<see cref="RenewCook"/>) before it is taken as gone. A task updates
    ///         every tick unless the AI is behind, and ending early is always safe.
    /// </summary>
    private static readonly TimeSpan CookLease = TimeSpan.FromSeconds(0.5);

    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<HandsComponent> _handsQuery = default!;
    [Dependency] private EntityQuery<TimerTriggerComponent> _timerTriggerQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        // A hold that ends this tick throws before the fuse is looked at, however short it is.
        UpdatesBefore.Add(typeof(TriggerSystem));
    }

    /// <summary>
    ///     Throws <paramref name="grenadeUid"/> at <paramref name="coordinates"/> as <see cref="TryThrow"/> does, or, for a thrower
    ///         that cooks and a grenade that can be, lights it and holds it first. See the remarks on the system.
    /// </summary>
    public NpcGrenadeThrowResult TryBeginThrow(EntityUid uid, EntityUid grenadeUid, EntityCoordinates coordinates)
    {
        if (_grenadeCookQuery.HasComp(uid))
            return NpcGrenadeThrowResult.Failed; // one at a time: the hand is full

        if (!TryGetCookTime(uid, grenadeUid, coordinates, out var holdTime, out var flightTime))
            return TryThrow(uid, grenadeUid, coordinates) ? NpcGrenadeThrowResult.Thrown : NpcGrenadeThrowResult.Failed;

        if (!_npcHandsSystem.TryTakeOut(uid, grenadeUid, out var takenOut))
            return NpcGrenadeThrowResult.Failed;

        if (!_triggerSystem.ActivateTimerTrigger(grenadeUid, uid))
        {
            _npcHandsSystem.PutBack(uid, takenOut);
            return NpcGrenadeThrowResult.Failed;
        }

        var curTime = _gameTiming.CurTime;

        // The fuse's own end, not what was worked out from its length: whatever set it going has the last word.
        var latestRelease = _timerTriggerQuery.GetComponent(grenadeUid).NextTrigger - TimeSpan.FromSeconds(flightTime + MinCookMargin);
        var releaseAt = TimeSpan.FromTicks(Math.Min((curTime + TimeSpan.FromSeconds(holdTime)).Ticks, latestRelease.Ticks));

        // Added once it is in the hand: going into a container takes it off again (OnThrownGrenadeInserted).
        var thrownGrenadeComponent = EnsureComp<NpcThrownGrenadeComponent>(grenadeUid);
        thrownGrenadeComponent.ThrowerUid = uid;
        thrownGrenadeComponent.Coordinates = coordinates;

        var cookComponent = EnsureComp<NpcGrenadeCookComponent>(uid);
        cookComponent.GrenadeUid = grenadeUid;
        cookComponent.Coordinates = coordinates;
        cookComponent.ReleaseAt = releaseAt;
        cookComponent.LeaseUntil = curTime + CookLease;
        cookComponent.TakenOut = takenOut;

        return NpcGrenadeThrowResult.Cooking;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is holding a grenade to cook it.
    /// </summary>
    public bool IsCooking(EntityUid uid)
    {
        return _grenadeCookQuery.HasComp(uid);
    }

    /// <summary>
    ///     The task that is cooking is still running. Without this, the hold ends within <see cref="CookLease"/>, by throwing.
    /// </summary>
    public void RenewCook(EntityUid uid)
    {
        if (_grenadeCookQuery.TryComp(uid, out var cookComponent))
            cookComponent.LeaseUntil = _gameTiming.CurTime + CookLease;
    }

    /// <summary>
    ///     Ends the hold now, if there is one: the grenade is thrown where it was meant to go, or dropped if it cannot be,
    ///         and what was in hand before comes back. Safe to call on anyone, at any time.
    /// </summary>
    public void EndCook(EntityUid uid)
    {
        if (!_grenadeCookQuery.TryComp(uid, out var cookComponent))
            return;

        var grenadeUid = cookComponent.GrenadeUid;
        var coordinates = cookComponent.Coordinates;
        var takenOut = cookComponent.TakenOut;

        // First: nothing below may find the hold still there and start another end to it.
        RemComp<NpcGrenadeCookComponent>(uid);

        // Out of the hand already, and not by this throw: dropped by a stun, knocked or taken out of it, or gone. Wherever it
        //      is, it is not coming down where it was meant to, and nobody is to go in on that spot when it goes off. The
        //      hands are put as they were all the same: the weapon is still to take up again.
        if (!Exists(grenadeUid) || !_handsSystem.IsHolding(uid, grenadeUid))
        {
            RemCompDeferred<NpcThrownGrenadeComponent>(grenadeUid);
            _npcHandsSystem.PutBack(uid, takenOut);
            return;
        }

        // The grenade is surely the item in the active hand, but a throw takes the active one, whatever is there.
        _npcHandsSystem.TryTakeOut(uid, grenadeUid, out _);

        // Whatever it threw a moment ago, this one goes now: the wait between throws is for one thing after another by
        //      choice, and the other way out of a hold cut short inside it is a lit grenade at its own feet.
        if (_handsQuery.TryComp(uid, out var handsComponent))
            handsComponent.NextThrowTime = TimeSpan.Zero;

        var thrown = _predictedHandsSystem.ThrowHeldItem(uid, coordinates) && !_handsSystem.IsHolding(uid, grenadeUid);
        if (!thrown && _handsSystem.IsHolding(uid, grenadeUid))
            _handsSystem.TryDrop(uid, grenadeUid, checkActionBlocker: false);

        // Dropped, it is not going where it was thrown at, and nobody is to go in on a spot it did not land on. Thrown, it is
        //      called out as it goes: squadmates had the whole hold to get clear already (TryGetLanding answers for a cook).
        if (!thrown)
            RemCompDeferred<NpcThrownGrenadeComponent>(grenadeUid);
        else
            _npcVoiceSystem.TrySpeakCallout(uid, NpcCallout.ThrowFrag);

        _npcHandsSystem.PutBack(uid, takenOut);
    }

    /// <summary>
    ///     How far to one side of the line it throws along a thrower steps while it holds a grenade, in tiles.
    /// </summary>
    private const float CookStepLength = 1.5f;

    /// <summary>
    ///     Somewhere for <paramref name="uid"/> to step to while it holds a grenade for a throw at
    ///         <paramref name="coordinates"/>, rather than stand still in the open for the whole of it: a little to one side of
    ///         the line it throws along (either, at random, then the other), where nothing is in the way of getting there or of
    ///         the throw from there. False if neither side will do. The throw is made from wherever it got to, a little further
    ///         off, which costs a few hundredths of a second of the margin and no more.
    /// </summary>
    public bool TryGetCookStep(EntityUid uid, EntityCoordinates coordinates, out EntityCoordinates step)
    {
        step = default;

        var from = _transformSystem.GetMapCoordinates(uid);
        var to = _transformSystem.ToMapCoordinates(coordinates);
        var offset = to.Position - from.Position;
        if (from.MapId != to.MapId || offset.LengthSquared() < 0.01f)
            return false;

        var across = Vector2.Normalize(new Vector2(-offset.Y, offset.X)) * CookStepLength;
        var side = _random.Prob(0.5f) ? 1f : -1f;

        for (var i = 0; i < 2; i++, side = -side)
        {
            var spot = from.Offset(across * side);
            if (!_npcLineOfFireSystem.IsClear(from, spot, CollisionGroup.MobMask) ||
                !_npcLineOfFireSystem.IsThrowClear(spot, to))
                continue;

            step = _transformSystem.ToCoordinates(spot);
            return true;
        }

        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var enumerator = EntityQueryEnumerator<NpcGrenadeCookComponent>();
        while (enumerator.MoveNext(out var uid, out var cookComponent))
        {
            var grenadeUid = cookComponent.GrenadeUid;

            // Anything that would stop a throw is the end of the hold: the grenade is thrown if it can be, dropped if not.
            if (curTime >= cookComponent.ReleaseAt ||
                curTime >= cookComponent.LeaseUntil ||
                !_mobStateSystem.IsAlive(uid) ||
                !Exists(grenadeUid) ||
                !_handsSystem.IsHolding(uid, grenadeUid) ||
                !_actionBlockerSystem.CanThrow(uid, grenadeUid))
            {
                EndCook(uid);
            }
        }
    }

    /// <summary>
    ///     How long to hold <paramref name="grenadeUid"/> for, and how long it will be in the air, if <paramref name="uid"/>
    ///         cooks and this is something to cook.
    /// </summary>
    private bool TryGetCookTime(EntityUid uid, EntityUid grenadeUid, EntityCoordinates coordinates, out float holdTime, out float flightTime)
    {
        holdTime = 0f;
        flightTime = 0f;

        if (!_grenadeThrowerQuery.TryComp(uid, out var grenadeThrowerComponent) ||
            !grenadeThrowerComponent.Cooks ||
            !_explosiveQuery.HasComp(grenadeUid) ||
            !_timerTriggerQuery.TryComp(grenadeUid, out var timerTriggerComponent) ||
            !_handsQuery.TryComp(uid, out var handsComponent))
            return false;

        var from = _transformSystem.GetMapCoordinates(uid);
        var to = _transformSystem.ToMapCoordinates(coordinates);
        if (from.MapId != to.MapId)
            return false;

        // Every tile of it at the speed it leaves at, none of the 0.8 the throwing system takes off for a throw that slides: the
        //   time is a little long, and the grenade is thrown a little early.
        var distance = MathF.Min((to.Position - from.Position).Length(), handsComponent.ThrowRange);
        flightTime = distance / MathF.Max(0.1f, handsComponent.BaseThrowspeed);

        var margin = MathF.Max(grenadeThrowerComponent.CookMargin, MinCookMargin);
        var spread = MathF.Max(0f, grenadeThrowerComponent.CookSpread);
        var fuse = (float) timerTriggerComponent.Delay.TotalSeconds;

        // At most what leaves the minimum, whatever the spread did, and never below nothing.
        holdTime = fuse - flightTime - margin + _random.NextFloat(-spread, spread);
        holdTime = MathF.Max(0f, MathF.Min(holdTime, fuse - flightTime - MinCookMargin));

        return holdTime >= MinCookTime;
    }
}
