using Robust.Shared.Timing;

namespace Content.Shared._KS14.Deferral;

/// <summary>
///     Used for <i>synchronously</i> deferring <see cref="Action"/>s onto running at a later gametick.
/// </summary>
/// <remarks>
///     This, as by default, updates in-prediction. The methods on this system can be safely called
///         statically; even when the system is not properly initialised or for not updating for any reason,
///         deferred operations will still resume on the first tick that they are allowed. Amazing right?
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

    /// <summary>
    ///     Queues something to run on the start of the next tick.
    /// </summary>
    public static void Add(Action action)
    {
        _deferredActions.Push(action);
    }

    /// <summary>
    ///     Queues something to run on the start of the first tick
    ///         after a given simulation-time.
    /// </summary>
    public static void Schedule(Action action, TimeSpan runBy)
    {
        _scheduledActions.Push((runBy, action));
    }
}
