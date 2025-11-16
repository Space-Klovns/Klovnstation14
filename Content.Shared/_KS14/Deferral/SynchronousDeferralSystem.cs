// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.Deferral;

/// <summary>
///     Used for <i>synchronously</i> deferring <see cref="Action"/>s onto running at a later gametick.
/// </summary>
/// <remarks>
///     This, as by default, updates in-prediction. The static methods on this system can be safely called,
///         even when the system is not properly initialised or not updating for any reason;
///         deferred operations will still resume on the first tick that they are allowed.
/// 
///     All cached actions are cleared when the system is shut-down.
/// </remarks>
// maybe TODO: some support for removing actions that are already queued?
public sealed class SynchronousDeferralSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    /// <summary>
    ///     Actions scheduled to run on the next tick.
    /// </summary>
    private static Stack<Action> _deferredActions = new();

    /// <summary>
    ///     Actions scheduled to run on the first tick
    ///         where <see cref="IGameTiming.CurTime"/>
    ///         passes the specified <see cref="TimeSpan"/>.  
    /// </summary>
    private static Stack<(TimeSpan, Action)> _scheduledActions = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_deferredActions.Count + _scheduledActions.Count == 0)
            return;

        while (_deferredActions.TryPop(out var action))
            action.Invoke();

        var gameTime = _gameTiming.CurTime;
        while (_scheduledActions.TryPop(out var scheduledAction))
        {
            if (gameTime < scheduledAction.Item1)
                continue;

            scheduledAction.Item2.Invoke();
        }
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Defer(Action action)
    {
        _deferredActions.Push(action);
    }

    /// <summary>
    ///     Queues something to run on the start of the first tick
    ///         after a given simulation-time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Schedule(Action action, TimeSpan runBy)
    {
        _scheduledActions.Push((runBy, action));
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
