using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Cultivation.Body;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Cultivation.UI;

/// <summary>
///     A body cultivator's tempered body at a glance: the stage, the tempering banked, and every part with its level,
///         how close it is to the next, and what it has unlocked. Powers that change how you play stand out in gold;
///         hover any power for what it does.
/// </summary>
public sealed partial class BodyPowersWindow : FancyWindow
{
    private static readonly Color Gold = Color.FromHex("#e8c060");
    private static readonly Color Muted = Color.FromHex("#8a8a8a");
    private static readonly Color Locked = Color.FromHex("#5c5c5c");

    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly RichTextLabel _header;
    private readonly BoxContainer _parts;
    private string _lastState = string.Empty;

    public BodyPowersWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("body-powers-window-title");
        MinSize = new Vector2(470, 420);
        SetSize = new Vector2(500, 560);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(10, 6), SeparationOverride = 6 };
        _header = new RichTextLabel();
        root.AddChild(_header);
        root.AddChild(new RichTextLabel { Text = Loc.GetString("body-powers-window-hint"), Modulate = Muted });

        _parts = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_parts);
        root.AddChild(scroll);
        ContentsContainer.AddChild(root);
    }

    public void Update(BodyCultivatorComponent body)
    {
        // Only rebuild when something shown has changed, so hovering a power keeps its tooltip.
        var state = $"{body.Stage}|{(int)body.Tempering}|" + string.Join(",", SharedBodyCultivationSystem.AllGroups.Select(g =>
            $"{SharedBodyCultivationSystem.GetLevel(body, g)}:{body.Progress.GetValueOrDefault(g):0.#}"));
        if (state == _lastState)
            return;

        _lastState = state;
        var cap = SharedBodyCultivationSystem.TemperingCapFor(body.Stage);
        _header.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString("body-powers-window-header",
            ("stage", body.Stage), ("stageName", Loc.GetString($"body-stage-{body.Stage}")),
            ("tempering", (int)body.Tempering), ("cap", (int)cap))));

        var powers = _prototypeManager.EnumeratePrototypes<BodyTemperingPowerPrototype>().ToList();
        var partCap = SharedBodyCultivationSystem.PartCap(body);
        _parts.RemoveAllChildren();
        foreach (var group in SharedBodyCultivationSystem.AllGroups)
            _parts.AddChild(PartRow(body, group, partCap, powers.Where(p => p.Group == group).OrderBy(p => p.Level).ToList()));
    }

    private Control PartRow(BodyCultivatorComponent body, BodyGroup group, int partCap, List<BodyTemperingPowerPrototype> powers)
    {
        var level = SharedBodyCultivationSystem.GetLevel(body, group);
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };

        // Name, level and progress towards the next level.
        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        var name = new RichTextLabel { MinWidth = 90 };
        name.SetMessage(FormattedMessage.FromMarkupOrThrow($"[bold]{Capitalize(Loc.GetString($"body-group-{group}"))}[/bold]"));
        top.AddChild(name);
        top.AddChild(new Label { Text = Loc.GetString("body-powers-window-level", ("level", level), ("max", BodyCultivatorComponent.MaxStage)), MinWidth = 70, FontColorOverride = Muted });

        if (level < BodyCultivatorComponent.MaxStage)
        {
            var cost = SharedBodyCultivationSystem.PartCost(level + 1);
            var progress = body.Progress.GetValueOrDefault(group);
            top.AddChild(new ProgressBar
            {
                MinValue = 0, MaxValue = cost, Value = progress,
                HorizontalExpand = true, MinHeight = 8, VerticalAlignment = VAlignment.Center,
                ToolTip = level >= partCap
                    ? Loc.GetString("body-powers-window-capped")
                    : Loc.GetString("body-powers-window-progress", ("progress", (int)progress), ("cost", cost)),
            });
        }
        else
        {
            top.AddChild(new Label { Text = Loc.GetString("body-powers-window-perfected"), FontColorOverride = Gold });
        }

        row.AddChild(top);

        // Unlocked powers as short names (gold when major), then the next one greyed out.
        var chips = new GridContainer { Columns = 3, HSeparationOverride = 10, VSeparationOverride = 0, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var power in powers.Where(p => p.Level <= level))
            chips.AddChild(Chip(power, unlocked: true));

        if (powers.FirstOrDefault(p => p.Level > level) is { } next)
            chips.AddChild(Chip(next, unlocked: false));

        if (chips.ChildCount > 0)
            row.AddChild(chips);

        return row;
    }

    private Control Chip(BodyTemperingPowerPrototype power, bool unlocked)
    {
        var name = Loc.GetString(power.Name);
        string markup;
        if (!unlocked)
            markup = $"[color={Locked.ToHex()}]{Loc.GetString("body-powers-window-next", ("power", name), ("level", power.Level))}[/color]";
        else if (power.Major)
            markup = $"[bold][color={Gold.ToHex()}]{name}[/color][/bold]";
        else
            markup = name;

        var label = new RichTextLabel
        {
            MouseFilter = MouseFilterMode.Stop,
            ToolTip = Loc.GetString(power.Description),
        };
        label.SetMessage(FormattedMessage.FromMarkupOrThrow(markup));
        return label;
    }

    private static string Capitalize(string text)
    {
        // Upper-case the first letter without joining a char to a string, which the client sandbox forbids.
        return text.Length == 0 ? text : text[..1].ToUpperInvariant() + text[1..];
    }
}
