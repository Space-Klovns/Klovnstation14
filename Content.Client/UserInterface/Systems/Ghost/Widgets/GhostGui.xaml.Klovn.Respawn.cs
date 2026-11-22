using Content.Client.Stylesheets;
using Content.Shared._KS14.GhostRespawn;

namespace Content.Client.UserInterface.Systems.Ghost.Widgets;

public sealed partial class GhostGui
{
    [Dependency] private Robust.Shared.Timing.IGameTiming _gameTiming = default!; // KS14

    public TimeSpan? RespawnTime { get; private set; }

    /// <summary>
    ///     What holds the respawn back past <see cref="RespawnTime"/>, if anything; its reason is the button's tooltip.
    /// </summary>
    public GhostRespawnGate? RespawnGate { get; private set; }

    public bool AlertedForRespawn { get; private set; }

    public void SetRespawnsEnabled(bool value)
        => GhostRespawnButton.Visible = value;

    public void SetRespawnTime(TimeSpan? time, GhostRespawnGate? gate)
    {
        RespawnTime = time;
        RespawnGate = time == null ? null : gate;
        SetAlerted(false);

        GhostRespawnButton.ToolTip = RespawnGate is { } shownGate ? Loc.GetString(shownGate.Reason) : null;
    }

    private void SetAlerted(bool alerted)
    {
        if (AlertedForRespawn == alerted)
            return;

        AlertedForRespawn = alerted;
        if (alerted)
            GhostRespawnButton.StyleClasses.Add(StyleClass.Negative);
        else
            GhostRespawnButton.StyleClasses.Remove(StyleClass.Negative);
    }

    protected override void FrameUpdate(Robust.Shared.Timing.FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (RespawnTime == null)
        {
            GhostRespawnButton.Disabled = true;
            GhostRespawnButton.Text = Loc.GetString("ghost-gui-respawn-button-disabled");
            SetAlerted(false);
            return;
        }

        var curTime = _gameTiming.CurTime;

        // Held back past the timer: count down to whichever lets go last.
        if (RespawnGate is { } gate && (gate.OpensAt is not { } opensAt || opensAt > RespawnTime.Value))
        {
            GhostRespawnButton.Disabled = true;
            GhostRespawnButton.Text = GetGatedText(gate, curTime);
            SetAlerted(false);
            return;
        }

        var secondsLeft = (RespawnTime.Value - curTime).TotalSeconds;
        if (secondsLeft > 0f)
        {
            GhostRespawnButton.Text = Loc.GetString("ghost-gui-respawn-button-wait", ("seconds", $"{secondsLeft:0.00}"));
            GhostRespawnButton.Disabled = true;
        }
        else
        {
            GhostRespawnButton.Disabled = false;
            if (AlertedForRespawn)
                return;

            GhostRespawnButton.Text = Loc.GetString("ghost-gui-respawn-button-now");
            SetAlerted(true);
        }
    }

    /// <summary>
    ///     The button's text while a gate holds the respawn back: a countdown to when it opens, if that's known.
    /// </summary>
    public string GetGatedText(GhostRespawnGate gate, TimeSpan curTime)
    {
        if (gate.OpensAt is not { } opensAt)
            return Loc.GetString("ghost-gui-respawn-button-gated");

        var left = opensAt - curTime;
        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;

        // Rounded up, so it never reads 0:00 while still closed.
        var totalSeconds = (int)Math.Ceiling(left.TotalSeconds);
        return Loc.GetString("ghost-gui-respawn-button-gated-wait", ("time", $"{totalSeconds / 60}:{totalSeconds % 60:00}"));
    }
}
