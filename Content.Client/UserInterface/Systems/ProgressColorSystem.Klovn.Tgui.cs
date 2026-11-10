using Content.Client._KS14.Tgui;

namespace Content.Client.UserInterface.Systems;

// Partial so progress shown in the world (do-afters, health bars) can use tgui's colours while the theme is on.
public sealed partial class ProgressColorSystem
{
    private bool _ksTgui;

    /// <summary>
    ///     Bad at nothing, through average, to good, as tgui's ProgressBar ranges are usually set.
    /// </summary>
    private static Color KsTguiProgressColor(float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);

        return progress < 0.5f
            ? Color.InterpolateBetween(KsTguiPalettes.Bad, KsTguiPalettes.Average, progress * 2f)
            : Color.InterpolateBetween(KsTguiPalettes.Average, KsTguiPalettes.Good, (progress - 0.5f) * 2f);
    }
}
