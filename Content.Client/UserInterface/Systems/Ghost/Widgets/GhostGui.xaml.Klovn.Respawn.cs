using Content.Client.Stylesheets;

namespace Content.Client.UserInterface.Systems.Ghost.Widgets;

public sealed partial class GhostGui
{
    [Dependency] private Robust.Shared.Timing.IGameTiming _gameTiming = default!; // KS14

    public TimeSpan? RespawnTime = null;
    public bool AlertedForRespawn = false;

    public void SetRespawnsEnabled(bool value)
        => GhostRespawnButton.Visible = value;

    protected override void FrameUpdate(Robust.Shared.Timing.FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (RespawnTime == null)
        {
            GhostRespawnButton.Disabled = true;
            GhostRespawnButton.Text = Loc.GetString("ghost-gui-respawn-button-disabled");

            if (AlertedForRespawn)
            {
                AlertedForRespawn = false;
                GhostRespawnButton.StyleClasses.Remove(StyleClass.Negative);
            }

            return;
        }

        var secondsLeft = (RespawnTime.Value - _gameTiming.CurTime).TotalSeconds;
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
            GhostRespawnButton.StyleClasses.Add(StyleClass.Negative);
            AlertedForRespawn = true;
        }
    }
}
