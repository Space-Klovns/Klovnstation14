using System.Runtime.CompilerServices;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.Deferral;

/// <summary>
///     Used for <i>synchronously</i> deferring <see cref="Action"/>s onto running at a later gametick.
/// </summary>
/// <remarks>
///     This, as by default, updates in-prediction. Deferred operations run on the first tick that they are
///         allowed.
///
///     The queues belong to the system instance, not to the type: the static methods resolve the calling side's
///         instance through IoC, which is per-thread. Integration tests run a client and a server in one process on
///         separate threads, and a static queue let each side run the other's actions - and corrupt the
///         non-thread-safe stacks by pushing and popping them concurrently.
///
///     All cached actions are cleared when the system is shut-down.
/// </remarks>
// maybe TODO: some support for removing actions that are already queued?
public sealed partial class SynchronousDeferralSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    /// <summary>
    ///     Actions scheduled to run on the next tick.
    /// </summary>
    private readonly Stack<Action> _deferredActions = new();

    /// <summary>
    ///     Actions scheduled to run on the first tick
    ///         where <see cref="IGameTiming.CurTime"/>
    ///         passes the specified <see cref="TimeSpan"/>.
    /// </summary>
    private readonly List<(TimeSpan RunBy, Action Action)> _scheduledActions = new();

    /// <summary>
    ///     Scratch list for the scheduled actions due this tick. They are taken out of
    ///         <see cref="_scheduledActions"/> before any of them run, since running one may schedule another.
    /// </summary>
    private readonly List<Action> _dueActions = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_deferredActions.Count + _scheduledActions.Count == 0)
            return;

        while (_deferredActions.TryPop(out var action))
            action.Invoke();

        // Actions that are not due yet stay queued. This used to pop every scheduled action and skip the ones not
        //      yet due, which dropped them for good - an explosion's client-side shockwave was never deleted.
        var gameTime = _gameTiming.CurTime;
        _dueActions.Clear();
        for (var i = _scheduledActions.Count - 1; i >= 0; i--)
        {
            var (runBy, scheduledAction) = _scheduledActions[i];
            if (gameTime < runBy)
                continue;

            _dueActions.Add(scheduledAction);
            _scheduledActions.RemoveAt(i);
        }

        foreach (var dueAction in _dueActions)
            dueAction.Invoke();

        _dueActions.Clear();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _deferredActions.Clear();
        _scheduledActions.Clear();
    }

    /// <summary>
    ///     Queues something to run on the start of the next tick.
    /// </summary>
    public static void Defer(Action action)
    {
        GetInstance()._deferredActions.Push(action);
    }

    /// <summary>
    ///     Queues something to run on the start of the first tick
    ///         after a given simulation-time.
    /// </summary>
    public static void Schedule(Action action, TimeSpan runBy)
    {
        GetInstance()._scheduledActions.Add((runBy, action));
    }

    /// <summary>
    ///     The calling side's instance of this system - IoC is per-thread, so this is the client's on the client
    ///         thread and the server's on the server thread.
    /// </summary>
    private static SynchronousDeferralSystem GetInstance()
    {
        return IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SynchronousDeferralSystem>();
    }

    /// <summary>
    ///     Queues something to run on the start of the first tick
    ///         after a given delay starting from when the method
    ///         is called, in simulation-time.
    /// </summary>
    /// <remarks>
    ///     Analogous to <see cref="Schedule(Action, TimeSpan)"/>,
    ///         with `runBy` specified as `<see cref="IGameTiming.CurTime"/>
    ///         + <paramref name="delay"/>`. However, this isn't static.
    /// </remarks>
    public void ScheduleForward(Action action, TimeSpan delay)
    {
        Schedule(action, _gameTiming.CurTime + delay);
    }

    /// <summary>
    ///     Constructs a <see cref="Action"/> that raises a local by-value event
    ///         on an entity. This does not actually defer it.
    /// </summary>
    /// <remarks>
    ///     Uses RaiseLocalEvent.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Action ConstructValueEventDispatcher<TEvent>(EntityUid uid, TEvent args, bool broadcast = false)
        where TEvent : notnull
    {
        return () => EntityManager.EventBus.RaiseLocalEvent(uid, args, broadcast);
    }
}
