using System.Globalization;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     Which way a <see cref="KsTguiTimeDisplay"/> counts by itself.
/// </summary>
public enum KsTguiTimeAuto : byte
{
    None,
    Up,
    Down,
}

/// <summary>
///     tgui-core's TimeDisplay: a time written as <c>HH:MM:SS</c>, or as <c>1h2m3s</c> with <see cref="ShortFormat"/>.
///         With <see cref="Auto"/> it counts up or down by a second every second by itself, down to zero, and starts
///         again from a <see cref="Value"/> that is not the one it was last given.
/// </summary>
public sealed class KsTguiTimeDisplay : Label
{
    /// <summary>
    ///     The 1 second of tgui's interval, and how far each tick moves the time.
    /// </summary>
    private static readonly TimeSpan TickLength = TimeSpan.FromSeconds(1);

    private TimeSpan _value;
    private TimeSpan _shown;
    private TimeSpan? _lastGiven;
    private TimeSpan _tickHeld;
    private KsTguiTimeAuto _auto;
    private bool _shortFormat;

    public KsTguiTimeDisplay()
    {
        Refresh();
    }

    /// <summary>
    ///     Replaces the default and short formats, as tgui's <c>format</c> prop.
    /// </summary>
    public Func<TimeSpan, string>? Format { get; set; }

    /// <summary>
    ///     tgui's <c>value</c>. Giving the value it already has does not restart a count.
    /// </summary>
    public TimeSpan Value
    {
        get => _value;
        set
        {
            _value = value;
            if (_lastGiven != value)
            {
                _lastGiven = value;
                _shown = value;
                _tickHeld = TimeSpan.Zero;
            }

            Refresh();
        }
    }

    /// <summary>
    ///     The time on the display now, which is <see cref="Value"/> unless it has counted on.
    /// </summary>
    public TimeSpan Shown => _shown;

    public KsTguiTimeAuto Auto
    {
        get => _auto;
        set
        {
            if (_auto == value)
                return;

            _auto = value;
            _tickHeld = TimeSpan.Zero;
        }
    }

    /// <summary>
    ///     tgui's <c>"short"</c> format.
    /// </summary>
    public bool ShortFormat
    {
        get => _shortFormat;
        set
        {
            _shortFormat = value;
            Refresh();
        }
    }

    /// <summary>
    ///     tgui-core's <c>formatTime</c>, in whole seconds rounded down. A time below zero is written as zero.
    /// </summary>
    public static string FormatTime(TimeSpan time, bool shortFormat = false)
    {
        var totalSeconds = time > TimeSpan.Zero ? time.Ticks / TimeSpan.TicksPerSecond : 0L;
        var hours = totalSeconds / 3600L;
        var minutes = totalSeconds % 3600L / 60L;
        var seconds = totalSeconds % 60L;

        if (shortFormat)
        {
            var hourText = hours > 0L ? $"{Whole(hours)}h" : string.Empty;
            var minuteText = minutes > 0L ? $"{Whole(minutes)}m" : string.Empty;
            var secondText = seconds > 0L ? $"{Whole(seconds)}s" : string.Empty;
            return hourText + minuteText + secondText;
        }

        return $"{Whole(hours).PadLeft(2, '0')}:{Whole(minutes).PadLeft(2, '0')}:{Whole(seconds).PadLeft(2, '0')}";
    }

    private static string Whole(long number)
    {
        return number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     One tick of tgui's interval: a second on or off, never below zero.
    /// </summary>
    public void Tick()
    {
        if (_auto == KsTguiTimeAuto.None)
            return;

        var next = _auto == KsTguiTimeAuto.Up ? _shown + TickLength : _shown - TickLength;
        _shown = next > TimeSpan.Zero ? next : TimeSpan.Zero;
        Refresh();
    }

    private void Refresh()
    {
        Text = Format != null ? Format(_shown) : FormatTime(_shown, shortFormat: _shortFormat);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_auto == KsTguiTimeAuto.None)
            return;

        _tickHeld += TimeSpan.FromSeconds(args.DeltaSeconds);
        while (_tickHeld >= TickLength)
        {
            _tickHeld -= TickLength;
            Tick();
        }
    }
}
