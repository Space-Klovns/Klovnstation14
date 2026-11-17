using Content.Client._KS14.Tgui.Controls;

namespace Content.Client._FarHorizons.Power.UI;

public sealed partial class NuclearReactorWindow
{
    /// <summary>
    ///     The silence button's <c>Modulate</c> in the XAML, which colours it under Baron.
    /// </summary>
    public static readonly Color KsSilenceBaronModulate = Color.FromHex("#C21339");

    /// <summary>
    ///     Under tgui the silence button sits in hazard stripes as tgui's red button, so the XAML's tint, which would darken
    ///         that red and its label, is taken off while the stripes are shown and put back when they aren't.
    /// </summary>
    private void KsInitializeSilenceHazard()
    {
        // Baron has no rule for the class, so it can stay on.
        SilenceAlarms.AddStyleClass(KsTguiColors.ButtonClass(KsTguiColor.Red));

        SilenceHazard.ShownChanged += KsApplySilenceHazard;
        KsApplySilenceHazard(SilenceHazard.Shown);
    }

    private void KsApplySilenceHazard(bool shown)
    {
        SilenceAlarms.Modulate = shown ? Color.White : KsSilenceBaronModulate;
    }
}
