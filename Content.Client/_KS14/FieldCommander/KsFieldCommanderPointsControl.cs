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
    private const float ChangeLifetime = 2.5f;
    private const float ChangeRise = 36f;
    private const float ChangeSpacing = 22f;
    private const float TopMargin = 72f;

    private static readonly Color GainColor = Color.FromHex("#7CDB6A");
    private static readonly Color LossColor = Color.FromHex("#F05A4F");

    private readonly Label _totalLabel;
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
            FontColorShadowOverride = Color.Black,
            MouseFilter = MouseFilterMode.Ignore,
        };

        AddChild(_totalLabel);
    }

    public void SetTotal(int total)
    {
        if (_shownTotal == total)
            return;

        _shownTotal = total;
        _totalLabel.Text = Loc.GetString("ks-fieldcommander-points-total", ("points", total));
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
            FontColorShadowOverride = Color.Black,
            MouseFilter = MouseFilterMode.Ignore,
        };

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
                TopMargin + _totalLabel.DesiredSize.Y + 4f + slot * ChangeSpacing - progress * ChangeRise));

            // Holds for the first half, then fades out.
            label.Modulate = Color.White.WithAlpha(Math.Clamp(2f - progress * 2f, 0f, 1f));
        }
    }
}
