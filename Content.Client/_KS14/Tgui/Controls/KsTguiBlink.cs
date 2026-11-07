using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's Blink: hides its content for <see cref="Time"/> milliseconds out of every <see cref="Interval"/> plus
///         <see cref="Time"/>, with hard cuts and its room kept, as <c>visibility: hidden</c> does. tgui's timer fires first
///         after one whole period, so the content starts visible for <c>Interval + Time</c>: with the defaults, 2 seconds on,
///         then 1 off, 1 on, 1 off.
/// </summary>
public sealed class KsTguiBlink : Control
{
    private float _elapsedSeconds;
    private int _interval = 1000;
    private int _time = 1000;
    private bool _hidden;

    /// <summary>
    ///     tgui's <c>interval</c>, in milliseconds: how long it stays visible between blinks.
    /// </summary>
    public int Interval
    {
        get => _interval;
        set
        {
            _interval = value;
            Restart();
        }
    }

    /// <summary>
    ///     tgui's <c>time</c>, in milliseconds: how long it is hidden.
    /// </summary>
    public int Time
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
    ///     Whether the content is hidden this many seconds after the timer started.
    /// </summary>
    public static bool IsHidden(float elapsedSeconds, int intervalMilliseconds, int timeMilliseconds)
    {
        var period = (float)(intervalMilliseconds + timeMilliseconds) / 1000f;
        if (period <= 0f || elapsedSeconds < period)
            return false;

        return elapsedSeconds % period < (float)timeMilliseconds / 1000f;
    }

    /// <summary>
    ///     Moves the timer on by this many seconds.
    /// </summary>
    public void Advance(float seconds)
    {
        _elapsedSeconds += seconds;
        SetHidden(IsHidden(_elapsedSeconds, _interval, _time));
    }

    private void Restart()
    {
        // tgui clears its timer and starts a new one when either prop changes.
        _elapsedSeconds = 0f;
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

        Advance(args.DeltaSeconds);
    }
}
