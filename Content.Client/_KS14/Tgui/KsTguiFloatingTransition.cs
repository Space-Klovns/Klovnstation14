namespace Content.Client._KS14.Tgui;

/// <summary>
///     tgui-core's <c>Floating</c> open and close transition for a popup that stays in the tree while it runs: <c>opacity</c>
///         and <c>transform</c> over <c>--floating-transition-time</c> (0.2 s) with CSS <c>ease</c>, between
///         <c>data-transition="close"</c> (opacity 0, <c>scale(0.9)</c>) and <c>"open"</c> (opacity 1, <c>scale(1)</c>).
///         A CSS transition that is told a new target part way restarts from the value it has reached, with the whole time
///         again, which is what <see cref="SetOpen"/> does.
/// </summary>
/// <remarks>
///     The dropdown does the same in <see cref="KsTguiFeelUIController"/> (opening) and <see cref="Backdrop.KsTguiDropdownGhost"/>
///         (closing, because the engine takes its popup out of the tree at once). The numbers are those.
/// </remarks>
public sealed class KsTguiFloatingTransition
{
    private float _from;
    private float _to;
    private float _elapsed = Backdrop.KsTguiDropdownGhost.Seconds;

    /// <summary>
    ///     How open it is now, 0 (closed) to 1 (open).
    /// </summary>
    public float Value { get; private set; }

    /// <summary>
    ///     Whether the target is open.
    /// </summary>
    public bool Open => _to >= 1f;

    /// <summary>
    ///     Whether it has reached its target.
    /// </summary>
    public bool Settled => _elapsed >= Backdrop.KsTguiDropdownGhost.Seconds;

    /// <summary>
    ///     <c>opacity</c>.
    /// </summary>
    public float Alpha => Value;

    /// <summary>
    ///     <c>scale()</c>: 0.9 closed, 1 open.
    /// </summary>
    public float Scale => Backdrop.KsTguiDropdownGhost.EndScale + (1f - Backdrop.KsTguiDropdownGhost.EndScale) * Value;

    /// <summary>
    ///     Aims at open or closed. Aiming where it already is changes nothing; otherwise it goes from the current value.
    /// </summary>
    public void SetOpen(bool open)
    {
        if (open == Open)
            return;

        _from = Value;
        _to = open ? 1f : 0f;
        _elapsed = 0f;
    }

    /// <summary>
    ///     Closed and at rest, as new.
    /// </summary>
    public void Reset()
    {
        _from = Value;
        _to = 0f;
        Value = 0f;
        _elapsed = Backdrop.KsTguiDropdownGhost.Seconds;
    }

    public void Advance(float deltaSeconds)
    {
        if (Settled)
            return;

        _elapsed += deltaSeconds;
        var progress = MathF.Min(_elapsed / Backdrop.KsTguiDropdownGhost.Seconds, 1f);
        Value = progress >= 1f ? _to : _from + (_to - _from) * KsTguiFeelUIController.CssEase(progress);
    }
}
