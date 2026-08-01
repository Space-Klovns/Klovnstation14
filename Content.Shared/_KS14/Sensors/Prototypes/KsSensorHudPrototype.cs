using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Sensors.Prototypes;

/// <summary>
///     A sinusoidal pulse multiplier for the radar HUD:
///         <c>Base + Amplitude * sin(t * Speed)</c>. Keep Amplitude &lt;= Base to stay
///         non-negative.
/// </summary>
[DataDefinition]
public sealed partial class KsPulse
{
    /// <summary>Angular speed fed into sin, in radians per second.</summary>
    [DataField]
    public float Speed = 3.2f;

    /// <summary>The pulse's mid-point multiplier (its DC offset).</summary>
    [DataField]
    public float Base = 0.55f;

    /// <summary>How far the multiplier swings above and below <see cref="Base"/>.</summary>
    [DataField]
    public float Amplitude = 0.45f;

    /// <summary>The pulse multiplier at time <paramref name="seconds"/>.</summary>
    public float Eval(double seconds) => Base + Amplitude * MathF.Sin((float) (seconds * Speed));
}

/// <summary>The curve shape of a <see cref="KsAnim"/>: presentation math, never game state.</summary>
public enum KsAnimCurve : byte
{
    /// <summary>Smooth breathing: Base + Amplitude * (0.5 + 0.5 * sin(2π * Speed * t)). Speed in Hz.</summary>
    Pulse = 0,

    /// <summary>Hard square blink: Base + Amplitude while the first half of each cycle, Base in the second. Speed in Hz.</summary>
    Blink = 1,

    /// <summary>Trigger-relative falloff: Base + Amplitude * exp(-Speed * secondsSinceTrigger). Speed in 1/s.</summary>
    Decay = 2,

    /// <summary>Sawtooth ramp 0..1 per cycle: Base + Amplitude * frac(Speed * t). Speed in Hz.</summary>
    Sweep = 3,
}

/// <summary>
///     A YAML-tunable instrument animation: one curve, its rate and its output range.
///         Evaluated per-frame in Draw by the client; the server sends nothing.
///         <see cref="Enabled"/> or the caller's reduce-motion switch freezes it to
///         <see cref="Still"/>.
/// </summary>
[DataDefinition]
public sealed partial class KsAnim
{
    [DataField]
    public KsAnimCurve Curve = KsAnimCurve.Pulse;

    /// <summary>When false this animation holds <see cref="Still"/>.</summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>Rate: Hz for the periodic curves, the exponential rate (1/s) for Decay.</summary>
    [DataField]
    public float Speed = 1f;

    /// <summary>Output floor (the value's DC offset).</summary>
    [DataField]
    public float Base;

    /// <summary>How far the output rises above <see cref="Base"/>.</summary>
    [DataField]
    public float Amplitude = 1f;

    /// <summary>
    ///     The motionless stand-in when the animation is disabled or reduce-motion is
    ///         on: full-on for the periodic curves (a steady light loses no
    ///         information), settled for Decay, rest for Sweep.
    /// </summary>
    public float Still => Curve switch
    {
        KsAnimCurve.Decay => Base,
        KsAnimCurve.Sweep => Base,
        _ => Base + Amplitude,
    };

    /// <summary>
    ///     The animation's value at wall-clock time <paramref name="seconds"/> (for
    ///         the periodic curves) or seconds SINCE THE TRIGGER (for Decay).
    /// </summary>
    public float Eval(double seconds, bool reducedMotion)
    {
        return Eval(seconds, reducedMotion, Speed);
    }

    /// <summary>
    ///     As <see cref="Eval(double, bool)"/> but at a caller-supplied rate: the
    ///         RWR threat strobe keeps its curve/range here while each channel
    ///         prototype sets how fast it blinks.
    /// </summary>
    public float Eval(double seconds, bool reducedMotion, float speed)
    {
        if (!Enabled || reducedMotion)
            return Still;

        var t = (float) seconds;
        return Curve switch
        {
            KsAnimCurve.Pulse => Base + Amplitude * (0.5f + 0.5f * MathF.Sin(MathF.Tau * speed * t)),
            KsAnimCurve.Blink => Base + (Frac(speed * t) < 0.5f ? Amplitude : 0f),
            KsAnimCurve.Decay => Base + Amplitude * MathF.Exp(-speed * MathF.Max(0f, t)),
            _ => Base + Amplitude * Frac(speed * t),
        };
    }

    private static float Frac(float v) => v - MathF.Floor(v);
}

/// <summary>
///     One instrument screen's colour set, keyed by screen id on
///         <see cref="KsSensorHudPrototype.Screens"/> so each tab (RADAR, ELINT, ...)
///         carries its own palette without code changes.
/// </summary>
[DataDefinition]
public sealed partial class KsInstrumentPalette
{
    /// <summary>Panel borders, titles and primary chrome.</summary>
    [DataField]
    public Color Accent = Color.FromHex("#FFC000");

    /// <summary>Secondary chrome: inactive tabs, unselected rows, dividers.</summary>
    [DataField]
    public Color AccentDim = Color.FromHex("#8A6A10");

    /// <summary>Screen/panel background fill.</summary>
    [DataField]
    public Color Background = Color.FromHex("#0A0C08");

    /// <summary>Primary readout text.</summary>
    [DataField]
    public Color Text = Color.FromHex("#FFD860");

    /// <summary>Secondary/unresolved readout text.</summary>
    [DataField]
    public Color TextDim = Color.FromHex("#907830");

    /// <summary>Alerts: JAMMED, threat lines, destructive actions.</summary>
    [DataField]
    public Color Warning = Color.FromHex("#FF3030");

    /// <summary>Positive states: LIVE tracks, completed analysis, links up.</summary>
    [DataField]
    public Color Good = Color.FromHex("#40FF80");
}

/// <summary>
///     The instrument shell's chrome colours: window bezel, title bar, tab-strip
///         buttons (pressed, unpressed and disabled tiers) and screen action buttons.
///         Baked into the GLOBAL stylesheet by <c>KsInstrumentSheetlet.GetRules</c>,
///         which builds once before PostInit, so these are session-fixed: a prototype
///         hot-reload restyles the per-screen palettes but never this block.
/// </summary>
[DataDefinition]
public sealed partial class KsInstrumentChrome
{
    /// <summary>The window's own backing fill behind every screen.</summary>
    [DataField]
    public Color WindowBackground = Color.FromHex("#0C0A06");

    /// <summary>The window's outer bezel border (the instrument's amber frame).</summary>
    [DataField]
    public Color WindowBorder = Color.FromHex("#C8A030");

    [DataField]
    public Color TitleBackground = Color.FromHex("#141008");

    /// <summary>Title-bar text and the chrome buttons' resting text.</summary>
    [DataField]
    public Color TitleText = Color.FromHex("#E8C860");

    /// <summary>Resting (unpressed) tab-button fill.</summary>
    [DataField]
    public Color TabBackground = Color.FromHex("#141008");

    /// <summary>Resting (unpressed) tab-button border.</summary>
    [DataField]
    public Color TabBorder = Color.FromHex("#6A5518");

    /// <summary>Resting (unpressed) tab-button text: the middle brightness tier.</summary>
    [DataField]
    public Color TabText = Color.FromHex("#8A7430");

    /// <summary>Pressed (active) tab-button fill; also the hover fill.</summary>
    [DataField]
    public Color TabPressedBackground = Color.FromHex("#2A2008");

    /// <summary>Pressed (active) tab-button border; also the hover border.</summary>
    [DataField]
    public Color TabPressedBorder = Color.FromHex("#C8A030");

    /// <summary>Pressed (active) tab-button text: the brightest tier.</summary>
    [DataField]
    public Color TabPressedText = Color.FromHex("#FFD860");

    /// <summary>Disabled tab-button fill (no hover response by construction: the disabled draw mode is exclusive).</summary>
    [DataField]
    public Color TabDisabledBackground = Color.FromHex("#0E0B05");

    [DataField]
    public Color TabDisabledBorder = Color.FromHex("#3A2E0C");

    /// <summary>Disabled tab-button text: the darkest tier.</summary>
    [DataField]
    public Color TabDisabledText = Color.FromHex("#4A3A10");

    /// <summary>Resting action-button (FOCUS/CEASE, roster rows) fill.</summary>
    [DataField]
    public Color ActionBackground = Color.FromHex("#100D06");

    /// <summary>Resting action-button border.</summary>
    [DataField]
    public Color ActionBorder = Color.FromHex("#6A5518");

    /// <summary>Pressed/hovered action-button fill.</summary>
    [DataField]
    public Color ActionPressedBackground = Color.FromHex("#241C08");

    /// <summary>Pressed/hovered action-button border.</summary>
    [DataField]
    public Color ActionPressedBorder = Color.FromHex("#C8A030");

    [DataField]
    public Color ActionDisabledBackground = Color.FromHex("#0B0904");

    [DataField]
    public Color ActionDisabledBorder = Color.FromHex("#352A0C");

    [DataField]
    public Color ActionDisabledText = Color.FromHex("#4A3A10");
}

/// <summary>
///     Client-side colour and animation theme for the KS14 sensor radar HUD. One
///         singleton (id <c>Default</c>) holds the whole palette so the HUD can be
///         re-themed from YAML without touching client code. Consumed by
///         <see cref="Content.Shared._KS14.Sensors"/> radar controls; the server never
///         reads it.
/// </summary>
[Prototype]
public sealed partial class KsSensorHudPrototype : IPrototype
{
    /// <summary>The singleton theme id every KS radar/instrument control indexes.</summary>
    public static readonly ProtoId<KsSensorHudPrototype> DefaultId = "Default";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The base colour a contact and its coverage cone render in, keyed by sensor type.</summary>
    [DataField]
    public Dictionary<KsSensorType, Color> ContactColors = new();

    /// <summary>Colour for a contact whose sensor type has no entry in <see cref="ContactColors"/>.</summary>
    [DataField]
    public Color Fallback = Color.FromHex("#FFC000");

    /// <summary>Tint for an allied (datalink self-report) contact, overriding its sensor-type colour.</summary>
    [DataField]
    public Color Ally = Color.MediumSpringGreen;

    /// <summary>Brightness multiplier applied to a stale (memory-ghost) contact's colour.</summary>
    [DataField]
    public float DimFactor = 0.65f;

    /// <summary>Alpha a dimmed memory-ghost colour is drawn at.</summary>
    [DataField]
    public float DimAlpha = 0.7f;

    /// <summary>Readout label colour for a currently-tracked ("LIVE") contact.</summary>
    [DataField]
    public Color Live = Color.FromHex("#FFC000");

    /// <summary>Readout label colour for a memory-ghost ("last seen") contact.</summary>
    [DataField]
    public Color Memory = Color.FromHex("#8A8A8A");

    /// <summary>Alpha of a contact readout's secondary lines (coords/range, last-seen, resolved intel values).</summary>
    [DataField]
    public float ReadoutDetailAlpha = 0.8f;

    /// <summary>Alpha of an always-on roster line whose intel value is still unresolved.</summary>
    [DataField]
    public float ReadoutUnresolvedAlpha = 0.45f;

    /// <summary>
    ///     Lowest readout detail level at which a contact's name still draws. The three
    ///         readout lines the radar builds itself declare their level here, the way
    ///         each intel line declares its own on <see cref="KsSensorIntelPrototype.Detail"/>.
    /// </summary>
    [DataField]
    public KsContactDetail NameDetail = KsContactDetail.Basic;

    /// <summary>Lowest readout detail level at which the coordinates/range line still draws.</summary>
    [DataField]
    public KsContactDetail PositionDetail = KsContactDetail.Full;

    /// <summary>Lowest readout detail level at which a memory ghost's age line still draws.</summary>
    [DataField]
    public KsContactDetail AgeDetail = KsContactDetail.Full;

    /// <summary>Hover-tooltip primary text (the contact name).</summary>
    [DataField]
    public Color TooltipText = Color.White;

    /// <summary>Hover-tooltip secondary text (coordinates, range, source attribution).</summary>
    [DataField]
    public Color TooltipDetail = Color.LightGray;

    /// <summary>Hover-tooltip panel background fill.</summary>
    [DataField]
    public Color TooltipBackground = new(8, 12, 16, 235);

    /// <summary>Hover-tooltip panel border.</summary>
    [DataField]
    public Color TooltipBorder = Color.FromHex("#4A5A6A");

    /// <summary>The "you are here" own-radar position marker.</summary>
    [DataField]
    public Color SelfPosition = Color.Lime;

    /// <summary>Marker for the controlling console when it sits on a different grid (drawn sRGB).</summary>
    [DataField]
    public Color OffGrid = Color.Cyan;

    /// <summary>Radar-interest waypoint marker and label (drawn sRGB).</summary>
    [DataField]
    public Color Interest = Color.DarkGoldenrod;

    /// <summary>Fill alpha of a filled coverage cone, further scaled by the emitting pulse.</summary>
    [DataField]
    public float ConeFillAlpha = 0.07f;

    /// <summary>Outline alpha of a coverage cone, further scaled by the emitting pulse.</summary>
    [DataField]
    public float ConeLineAlpha = 0.45f;

    /// <summary>Blink of an active-emission (radar or jammer) coverage cone.</summary>
    [DataField]
    public KsPulse ConePulse = new();

    /// <summary>The "JAMMED" alarm text colour.</summary>
    [DataField]
    public Color JammedColor = Color.FromHex("#FF3030");

    /// <summary>Blink of the "JAMMED" alarm text.</summary>
    [DataField]
    public KsPulse JammedPulse = new() { Speed = 6f, Base = 0.6f, Amplitude = 0.4f };

    [DataField]
    public float JammedTextScale = 1.4f;

    /// <summary>
    ///     Per-screen instrument palettes, keyed by screen id ("shell", "esm", ...).
    ///         A missing key falls back to <see cref="GetScreen"/>'s stock palette.
    /// </summary>
    [DataField]
    public Dictionary<string, KsInstrumentPalette> Screens = new();

    /// <summary>The instrument shell's stylesheet-baked chrome colours (see <see cref="KsInstrumentChrome"/>).</summary>
    [DataField]
    public KsInstrumentChrome Chrome = new();

    /// <summary>Bearing-wedge alpha breathing, depth scaled by signal strength (a strong signal holds steady).</summary>
    [DataField]
    public KsAnim WedgeShimmer = new() { Curve = KsAnimCurve.Pulse, Speed = 0.7f, Base = 0.45f, Amplitude = 0.55f };

    /// <summary>The rotating corner brackets on a focus-analysed emitter (Sweep = one revolution per cycle).</summary>
    [DataField]
    public KsAnim FocusReticle = new() { Curve = KsAnimCurve.Sweep, Speed = 0.25f, Base = 0f, Amplitude = 1f };

    /// <summary>Brightness flash of a fresh emission-log row, decaying to its settled tone.</summary>
    [DataField]
    public KsAnim LogFlash = new() { Curve = KsAnimCurve.Decay, Speed = 1.2f, Base = 1f, Amplitude = 1.5f };

    /// <summary>The brief interlace flicker on an instrument tab switch (evaluated against seconds since the switch).</summary>
    [DataField]
    public KsAnim TabFlicker = new() { Curve = KsAnimCurve.Blink, Speed = 30f, Base = 0.4f, Amplitude = 0.6f };

    /// <summary>Seconds the tab-switch flicker runs before the screen settles.</summary>
    [DataField]
    public float TabFlickerDuration = 0.12f;

    /// <summary>
    ///     The RWR threat strobe's curve and brightness range. Its rate comes per
    ///         channel (<c>KsThreatChannelPrototype.BlinkHz</c> via the rate-override
    ///         <see cref="KsAnim.Eval(double, bool, float)"/>), so a higher-priority
    ///         channel blinks faster; the Speed here is only the fallback.
    /// </summary>
    [DataField]
    public KsAnim ThreatStrobe = new() { Curve = KsAnimCurve.Blink, Speed = 1f, Base = 0.35f, Amplitude = 0.65f };

    /// <summary>
    ///     Phosphor afterglow: a live contact's brightness multiplier, evaluated
    ///         against seconds since its last sweep refresh, so a track being painted
    ///         reads hotter than one coasting between sweeps.
    /// </summary>
    [DataField]
    public KsAnim Afterglow = new() { Curve = KsAnimCurve.Decay, Speed = 1.6f, Base = 1f, Amplitude = 0.6f };

    /// <summary>
    ///     Seconds a contact takes to fade from its live colour to the memory-ghost
    ///         tone after going stale (0 = snap; reduce-motion always snaps).
    /// </summary>
    [DataField]
    public float GhostFadeSeconds = 1.5f;

    /// <summary>
    ///     Seconds the contact-ping ring (first detection / live-again) takes to
    ///         expand and fade. 0 disables the ping; reduce-motion skips it.
    /// </summary>
    [DataField]
    public float PingSeconds = 0.8f;

    /// <summary>The contact-ping ring's starting alpha.</summary>
    [DataField]
    public float PingAlpha = 0.9f;

    /// <summary>
    ///     The instrument shell's power-on flicker, evaluated against seconds since
    ///         the window opened and enveloped over <see cref="BootDuration"/>.
    /// </summary>
    [DataField]
    public KsAnim Boot = new() { Curve = KsAnimCurve.Blink, Speed = 24f, Base = 0.3f, Amplitude = 0.7f };

    /// <summary>Seconds the boot flicker runs before the shell settles (0 disables it).</summary>
    [DataField]
    public float BootDuration = 0.7f;

    /// <summary>How many range rings the RADAR plot spreads across its current display range.</summary>
    [DataField]
    public int RangeRingCount = 4;

    private static readonly KsInstrumentPalette FallbackScreen = new();

    /// <summary>The instrument palette for a screen id, or a stock palette when unthemed.</summary>
    public KsInstrumentPalette GetScreen(string screen) =>
        Screens.TryGetValue(screen, out var palette) ? palette : FallbackScreen;

    /// <summary>The contact/cone colour for <paramref name="type"/>, or <see cref="Fallback"/>.</summary>
    public Color GetContactColor(KsSensorType type) =>
        ContactColors.TryGetValue(type, out var color) ? color : Fallback;

    /// <summary>Fades a colour to its memory-ghost form (dimmed brightness, reduced alpha).</summary>
    public Color Dim(Color color) =>
        new(color.R * DimFactor, color.G * DimFactor, color.B * DimFactor, DimAlpha);
}
