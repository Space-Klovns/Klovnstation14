using Content.Client.Message;
using Content.Shared._KS14.Atmos;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Atmos.UI;

/// <summary>
///     A line at the foot of a compressing device's window saying where its heat is going. Set <see cref="Device"/> and it
///         keeps itself up to date, so a window only has to place it.
/// </summary>
public sealed partial class KsPumpHeatFootnote : RichTextLabel
{
    [Dependency] private IEntityManager _entityManager = default!;

    /// <summary>
    ///     The device whose heat this shows.
    /// </summary>
    public EntityUid? Device { get; set; }

    private (float Gas, float Room, float Recovered)? _shownHeat;
    private bool _written;

    public KsPumpHeatFootnote()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        Refresh();
    }

    /// <summary>
    ///     Writes the line, if what it says has changed.
    /// </summary>
    public void Refresh()
    {
        (float Gas, float Room, float Recovered)? heat =
            _entityManager.TryGetComponent(Device, out KsPumpHeatComponent? pumpHeatComponent)
                ? (pumpHeatComponent.GasPower, pumpHeatComponent.RoomPower, pumpHeatComponent.RecoveredPower)
                : null;

        // Formatted only when it changes, which is once a second at most, not every frame.
        if (_written && heat == _shownHeat)
            return;

        _written = true;
        _shownHeat = heat;
        this.SetMarkup(Loc.GetString("ks-pump-heat-footnote", ("text", Format(heat))));
    }

    private static string Format((float Gas, float Room, float Recovered)? heat)
    {
        if (heat is not { } power)
            return Loc.GetString("ks-pump-heat-footnote-idle");

        return power.Recovered > 0f
            ? Loc.GetString("ks-pump-heat-footnote-recovering", ("gas", power.Gas), ("room", power.Room), ("recovered", power.Recovered))
            : Loc.GetString("ks-pump-heat-footnote-working", ("gas", power.Gas), ("room", power.Room));
    }
}
