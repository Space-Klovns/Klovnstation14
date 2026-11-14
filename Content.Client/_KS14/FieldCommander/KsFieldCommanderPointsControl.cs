using System.Numerics;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.FieldCommander;

/// <summary>
///     A points readout at the top of the screen, with each change rising out of it and fading away.
/// </summary>
public sealed class KsFieldCommanderPointsControl : LayoutContainer
{
    /// <summary>
    ///     How long a change stays on screen, in seconds.
    /// </summary>
    private const float ChangeLifetime = 2.5f;

    /// <summary>
    ///     How long a change takes to fade in, as a fraction of its life.
    /// </summary>
    private const float FadeInFraction = 0.08f;

    /// <summary>
    ///     How far a change rises over its life. It starts this far below where it ends, which is
    ///         <see cref="ChangeGap"/> under the total, so it never reaches the total.
    /// </summary>
    private const float ChangeRise = 18f;

    /// <summary>
    ///     Room left between the total and the highest a change rises to.
    /// </summary>
    private const float ChangeGap = 10f;

    /// <summary>
    ///     How far apart changes on screen at once are stacked.
    /// </summary>
    private const float ChangeSpacing = 24f;

    /// <summary>
    ///     How far down the screen the total sits, clear of the top bar.
    /// </summary>
    private const float TopMargin = 72f;

    /// <summary>
    ///     A dark rim around every letter, so the text reads on snow as well as on dark ground.
    /// </summary>
    private const float OutlineThickness = 2f;
    private static readonly Color OutlineColor = Color.Black.WithAlpha(0.9f);

    private static readonly Color GainColor = Color.FromHex("#7CDB6A");
    private static readonly Color LossColor = Color.FromHex("#F05A4F");

    private readonly Label _totalLabel;
    private readonly Label _waveLabel;
    private readonly Font _changeFont;
    private readonly List<(Label Label, float Age, int Slot)> _changes = [];

    private int? _shownTotal;

    public KsFieldCommanderPointsControl()
    {
        var resourceCache = IoCManager.Resolve<IResourceCache>();

        MouseFilter = MouseFilterMode.Ignore;
        SetAnchorPreset(this, LayoutPreset.Wide);

        _changeFont = resourceCache.NotoStack(variation: "Bold", size: 13);

        _totalLabel = new Label
        {
            FontOverride = resourceCache.NotoStack(variation: "Bold", size: 15),
            FontColorOverride = Color.White,
            OutlineThicknessOverride = OutlineThickness,
            OutlineColorOverride = OutlineColor,
            MouseFilter = MouseFilterMode.Ignore,
            Visible = false,
        };

        _waveLabel = new Label
        {
            FontOverride = resourceCache.NotoStack(variation: "Bold", size: 12),
            FontColorOverride = Color.FromHex("#E8C77A"),
            OutlineThicknessOverride = OutlineThickness,
            OutlineColorOverride = OutlineColor,
            MouseFilter = MouseFilterMode.Ignore,
            Visible = false,
        };

        AddChild(_waveLabel);
        AddChild(_totalLabel);
    }

    /// <summary>
    ///     What the wave line says now, or null if it's hidden, for tests.
    /// </summary>
    public string? WaveText => _waveLabel.Visible ? _waveLabel.Text : null;

    /// <summary>
    ///     Shows the wave line above the total, or hides it for null.
    /// </summary>
    public void SetWaveText(string? text)
    {
        _waveLabel.Visible = text != null;
        if (text != null && _waveLabel.Text != text)
            _waveLabel.Text = text;
    }

    /// <summary>
    ///     How many changes are on screen now, for tests.
    /// </summary>
    public int ChangeCount => _changes.Count;

    /// <summary>
    ///     Shows the player's points, or hides the readout for null.
    /// </summary>
    public void SetTotal(int? total)
    {
        if (_shownTotal == total)
            return;

        _shownTotal = total;
        _totalLabel.Visible = total != null;
        if (total is { } points)
            _totalLabel.Text = Loc.GetString("ks-fieldcommander-points-total", ("points", points));
    }

    public void AddChange(int delta, string reason)
    {
        var text = Loc.GetString(delta > 0 ? "ks-fieldcommander-points-gain" : "ks-fieldcommander-points-loss",
            ("amount", Math.Abs(delta)),
            ("reason", reason));

        var label = new Label
        {
            Text = text,
            FontOverride = _changeFont,
            FontColorOverride = delta > 0 ? GainColor : LossColor,
            OutlineThicknessOverride = OutlineThickness,
            OutlineColorOverride = OutlineColor,
            MouseFilter = MouseFilterMode.Ignore,
        };

        // Placed and transparent from the start, so it fades in where it belongs rather than popping in a frame late.
        label.Modulate = Color.White.WithAlpha(0f);
        AddChild(label);

        // The lowest slot no change still on screen is using, so that several at once stack rather than overlap.
        var slot = 0;
        while (_changes.Exists(change => change.Slot == slot))
            slot++;

        _changes.Add((label, 0f, slot));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _totalLabel.Measure(Vector2Helpers.Infinity);
        SetPosition(_totalLabel, new Vector2((Size.X - _totalLabel.DesiredSize.X) / 2f, TopMargin));

        _waveLabel.Measure(Vector2Helpers.Infinity);
        SetPosition(_waveLabel, new Vector2((Size.X - _waveLabel.DesiredSize.X) / 2f, TopMargin - _waveLabel.DesiredSize.Y));

        for (var i = _changes.Count - 1; i >= 0; i--)
        {
            var (label, age, slot) = _changes[i];
            age += args.DeltaSeconds;

            if (age >= ChangeLifetime)
            {
                label.Orphan();
                _changes.RemoveAt(i);
                continue;
            }

            _changes[i] = (label, age, slot);

            var progress = age / ChangeLifetime;
            label.Measure(Vector2Helpers.Infinity);
            SetPosition(label, new Vector2(
                (Size.X - label.DesiredSize.X) / 2f,
                TopMargin + _totalLabel.DesiredSize.Y + ChangeGap + slot * ChangeSpacing + (1f - progress) * ChangeRise));

            // Fades in quickly, holds, then fades out over the second half.
            var alpha = MathF.Min(progress / FadeInFraction, 2f - progress * 2f);
            label.Modulate = Color.White.WithAlpha(Math.Clamp(alpha, 0f, 1f));
        }
    }
}
