using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared.Wires;
using Robust.Client.Animations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Animations;

namespace Content.Client._KS14.Tgui.Windows.Wires;

/// <summary>
///     The wires window laid out after tgui's Wires.tsx: a notice with the board's name, a list of wires with Cut/Mend and
///         Pulse buttons, and the statuses below. SS14 has no detaching, so there is no Attach button.
/// </summary>
public sealed class KsTguiWiresWindow : FancyWindow
{
    private const float BlinkFastSeconds = 0.2f;
    private const float BlinkSlowSeconds = 0.8f;

    private readonly BoxContainer _root;
    private readonly KsTguiNoticeBox _notice;
    private readonly KsTguiLabeledList _wireList;
    private readonly KsTguiSection _statusSection;
    private readonly BoxContainer _statusBox;
    private readonly Label _serialLabel;

    public event Action<int, WiresAction>? OnAction;

    public KsTguiWiresWindow()
    {
        Title = Loc.GetString("ks-tgui-wires-title");
        SetSize = new Vector2(350f, 330f);
        MinSize = new Vector2(350f, 150f);

        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
        };

        _notice = new KsTguiNoticeBox { HorizontalExpand = true };
        _root.AddChild(_notice);

        var wireSection = new KsTguiSection { Fill = true, Scrollable = true, VerticalExpand = true };
        _wireList = new KsTguiLabeledList();
        wireSection.AddChild(_wireList);
        _root.AddChild(wireSection);

        _statusSection = new KsTguiSection();
        _statusBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _statusSection.AddChild(_statusBox);
        _serialLabel = new Label { Modulate = Color.Gray };
        _statusSection.AddChild(_serialLabel);
        _root.AddChild(_statusSection);

        ContentsContainer.AddChild(_root);
    }

    public void Populate(WiresBoundUserInterfaceState state)
    {
        _notice.Text = Loc.GetString("ks-tgui-wires-configuration", ("name", state.BoardName));

        _wireList.RemoveAllChildren();
        foreach (var wire in state.WiresList)
        {
            var wireId = wire.Id;
            var isCut = wire.IsCut;
            var row = _wireList.AddItem(wire.Color.Name(), textColor: wire.Color.ColorValue());
            row.Colon = false;

            var letter = new Label
            {
                Text = $"({wire.Letter.Letter()})",
                ToolTip = wire.Letter.Name(),
                Modulate = Color.Gray,
                MouseFilter = Control.MouseFilterMode.Stop,
            };
            row.AddChild(letter);

            var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            var cutButton = new KsTguiButton
            {
                Text = Loc.GetString(isCut ? "ks-tgui-wires-mend" : "ks-tgui-wires-cut"),
            };
            cutButton.OnPressed += _ => OnAction?.Invoke(wireId, isCut ? WiresAction.Mend : WiresAction.Cut);
            var pulseButton = new KsTguiButton { Text = Loc.GetString("ks-tgui-wires-pulse") };
            pulseButton.OnPressed += _ => OnAction?.Invoke(wireId, WiresAction.Pulse);
            buttons.AddChild(cutButton);
            buttons.AddChild(pulseButton);
            row.Buttons = buttons;
        }

        _statusBox.RemoveAllChildren();
        foreach (var status in state.Statuses)
        {
            if (status.Value is StatusLightData lightData)
                _statusBox.AddChild(CreateLight(lightData));
            else
                _statusBox.AddChild(new Label { Text = status.ToString() });
        }

        _serialLabel.Text = state.SerialNumber == null
            ? string.Empty
            : Loc.GetString("ks-tgui-wires-serial", ("serial", state.SerialNumber));
        _serialLabel.Visible = state.SerialNumber != null;
        _statusSection.Visible = state.Statuses.Length > 0 || state.SerialNumber != null;
    }

    private static Control CreateLight(StatusLightData data)
    {
        var icon = new KsTguiIcon
        {
            Glyph = KsTguiIcons.Circle,
            IconColor = data.State == StatusLightState.Off ? data.Color.WithAlpha(0.25f) : data.Color,
            VerticalAlignment = Control.VAlignment.Center,
        };

        Animation? animation = null;
        switch (data.State)
        {
            case StatusLightState.BlinkingFast:
                animation = CreateBlink(BlinkFastSeconds);
                break;
            case StatusLightState.BlinkingSlow:
                animation = CreateBlink(BlinkSlowSeconds);
                break;
        }

        if (animation != null)
        {
            icon.PlayAnimation(animation, "blink");
            icon.AnimationCompleted += name =>
            {
                if (name == "blink")
                    icon.PlayAnimation(animation, name);
            };
        }

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        row.AddChild(icon);
        row.AddChild(new Label { Text = data.Text });
        return row;
    }

    private static Animation CreateBlink(float seconds)
    {
        return new Animation
        {
            Length = TimeSpan.FromSeconds(seconds),
            AnimationTracks =
            {
                new AnimationTrackControlProperty
                {
                    Property = nameof(Control.Modulate),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White, 0f),
                        new AnimationTrackProperty.KeyFrame(Color.White, seconds * 0.45f),
                        new AnimationTrackProperty.KeyFrame(Color.Transparent, 0.05f * seconds),
                        new AnimationTrackProperty.KeyFrame(Color.Transparent, seconds * 0.45f),
                        new AnimationTrackProperty.KeyFrame(Color.White, 0.05f * seconds),
                    },
                },
            },
        };
    }
}
