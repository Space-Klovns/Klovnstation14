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
///     tgui-core's TimeDisplay: a time in deciseconds (tg's time unit) written as <c>HH:MM:SS</c>, or as <c>1h2m3s</c> with
///         <see cref="ShortFormat"/>. With <see cref="Auto"/> it counts up or down by ten every second by itself, down to 0,
///         and starts again from a <see cref="Value"/> that is not the one it was last given.
/// </summary>
public sealed class KsTguiTimeDisplay : Label
{
    /// <summary>
    ///     The 1 second of tgui's interval.
    /// </summary>
    private const float TickSeconds = 1f;

    private float _value;
    private float _shown;
    private float? _lastGiven;
    private float _tickHeld;
    private KsTguiTimeAuto _auto;
    private bool _shortFormat;

    public KsTguiTimeDisplay()
    {
        Refresh();
    }

    /// <summary>
    ///     Replaces the default and short formats, as tgui's <c>format</c> prop.
    /// </summary>
    public Func<float, string>? Format { get; set; }

    /// <summary>
    ///     tgui's <c>value</c>, in deciseconds. Giving the value it already has does not restart a count.
    /// </summary>
    public float Value
    {
        get => _value;
        set
        {
            _value = value;
            if (_lastGiven != value)
            {
                _lastGiven = value;
                _shown = float.IsFinite(value) ? value : 0f;
                _tickHeld = 0f;
            }

            Refresh();
        }
    }

    /// <summary>
    ///     The time on the display now, which is <see cref="Value"/> unless it has counted on.
    /// </summary>
    public float Shown => _shown;

    public KsTguiTimeAuto Auto
    {
        get => _auto;
        set
        {
            if (_auto == value)
                return;

            _auto = value;
            _tickHeld = 0f;
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
    ///     tgui-core's <c>formatTime</c>.
    /// </summary>
    public static string FormatTime(float deciseconds, bool shortFormat = false)
    {
        var totalSeconds = MathF.Floor(deciseconds / 10f);
        var hours = MathF.Floor(totalSeconds / 3600f);
        var minutes = MathF.Floor(totalSeconds % 3600f / 60f);
        var seconds = totalSeconds % 60f;

        if (shortFormat)
        {
            var hourText = hours > 0f ? $"{Whole(hours)}h" : string.Empty;
            var minuteText = minutes > 0f ? $"{Whole(minutes)}m" : string.Empty;
            var secondText = seconds > 0f ? $"{Whole(seconds)}s" : string.Empty;
            return hourText + minuteText + secondText;
        }

        return $"{Whole(hours).PadLeft(2, '0')}:{Whole(minutes).PadLeft(2, '0')}:{Whole(seconds).PadLeft(2, '0')}";
    }

    private static string Whole(float number)
    {
        return ((long)number).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     One tick of tgui's interval: ten deciseconds on or off, never below zero.
    /// </summary>
    public void Tick()
    {
        if (_auto == KsTguiTimeAuto.None)
            return;

        _shown = MathF.Max(0f, _shown + (_auto == KsTguiTimeAuto.Up ? 10f : -10f));
        Refresh();
    }

    private void Refresh()
    {
        if (!float.IsFinite(_value))
        {
            Text = string.Empty;
            return;
        }

        Text = Format != null ? Format(_shown) : FormatTime(_shown, _shortFormat);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_auto == KsTguiTimeAuto.None)
            return;

        _tickHeld += args.DeltaSeconds;
        while (_tickHeld >= TickSeconds)
        {
            _tickHeld -= TickSeconds;
            Tick();
        }
    }
}
