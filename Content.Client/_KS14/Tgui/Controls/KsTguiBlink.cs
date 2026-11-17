using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's Blink: hides its content for <see cref="Time"/> out of every <see cref="Interval"/> plus
///         <see cref="Time"/>, with hard cuts and its room kept, as <c>visibility: hidden</c> does. tgui's timer fires first
///         after one whole period, so the content starts visible for <c>Interval + Time</c>: with the defaults, 2 seconds on,
///         then 1 off, 1 on, 1 off.
/// </summary>
public sealed class KsTguiBlink : Control
{
    private TimeSpan _elapsed;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private TimeSpan _time = TimeSpan.FromSeconds(1);
    private bool _hidden;

    /// <summary>
    ///     tgui's <c>interval</c>: how long it stays visible between blinks.
    /// </summary>
    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            _interval = value;
            Restart();
        }
    }

    /// <summary>
    ///     tgui's <c>time</c>: how long it is hidden.
    /// </summary>
    public TimeSpan Time
    {
        get => _time;
        set
        {
            _time = value;
            Restart();
        }
    }

    public bool Hidden => _hidden;

    /// <summary>
    ///     Whether the content is hidden <paramref name="elapsed"/> after the timer started.
    /// </summary>
    public static bool IsHidden(TimeSpan elapsed, TimeSpan interval, TimeSpan time)
    {
        var period = interval + time;
        if (period <= TimeSpan.Zero || elapsed < period)
            return false;

        return TimeSpan.FromTicks(elapsed.Ticks % period.Ticks) < time;
    }

    /// <summary>
    ///     Moves the timer on.
    /// </summary>
    public void Advance(TimeSpan time)
    {
        _elapsed += time;
        SetHidden(IsHidden(_elapsed, _interval, _time));
    }

    private void Restart()
    {
        // tgui clears its timer and starts a new one when either prop changes.
        _elapsed = TimeSpan.Zero;
        SetHidden(false);
    }

    private void SetHidden(bool hidden)
    {
        if (_hidden == hidden)
            return;

        _hidden = hidden;
        Modulate = hidden ? Color.White.WithAlpha(0f) : Color.White;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        Advance(TimeSpan.FromSeconds(args.DeltaSeconds));
    }
}
