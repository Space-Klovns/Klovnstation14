using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Cultivation.UI;

/// <summary>
///     A qi cultivator's path at a glance: the realm they're in, their insight and qi, and every realm with the
///         techniques it opens, from the laws they hold and those every cultivator comes to. What's still ahead is
///         greyed out; hover any technique for what it does.
/// </summary>
public sealed partial class QiPathWindow : FancyWindow
{
    private static readonly Color Gold = Color.FromHex("#e8c060");
    private static readonly Color Muted = Color.FromHex("#8a8a8a");
    private static readonly Color Locked = Color.FromHex("#5c5c5c");

    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly RichTextLabel _header;
    private readonly BoxContainer _realms;
    private string _lastState = string.Empty;

    public QiPathWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("qi-path-window-title");
        MinSize = new Vector2(530, 400);
        SetSize = new Vector2(540, 560);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(10, 6), SeparationOverride = 6 };
        _header = new RichTextLabel();
        root.AddChild(_header);
        root.AddChild(new RichTextLabel { Text = Loc.GetString("qi-path-window-hint"), Modulate = Muted });

        _realms = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 14 };
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_realms);
        root.AddChild(scroll);
        ContentsContainer.AddChild(root);
    }

    public void Update(CultivatorComponent cultivator, float maxQi, float? insightRequired)
    {
        var state = $"{cultivator.Realm}|{(int)cultivator.Insight}|{(int)cultivator.Qi}|{string.Join(",", cultivator.Laws)}";
        if (state == _lastState)
            return;

        _lastState = state;
        _header.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString("qi-path-window-header",
            ("realm", Loc.GetString($"cultivation-realm-{cultivator.Realm}")),
            ("insight", (int)cultivator.Insight), ("required", insightRequired is { } required ? (int)required : 0),
            ("peak", insightRequired == null ? "true" : "false"),
            ("qi", (int)cultivator.Qi), ("maxQi", (int)maxQi))));

        // Every technique by the realm that opens it: the universal ones, then each law's.
        var byRealm = new Dictionary<CultivationRealm, List<EntProtoId>>();
        foreach (var (action, realm) in CultivationTechniqueLists.Universal)
            Add(byRealm, realm, action);

        foreach (var lawId in cultivator.Laws)
        {
            if (!_prototypeManager.TryIndex(lawId, out var law))
                continue;

            foreach (var technique in law.Techniques)
                Add(byRealm, technique.Realm, technique.Action);
        }

        _realms.RemoveAllChildren();
        foreach (var realm in Enum.GetValues<CultivationRealm>())
        {
            var reached = realm <= cultivator.Realm;
            var current = realm == cultivator.Realm;
            var section = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };

            var heading = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
            heading.AddChild(new Label
            {
                Text = Loc.GetString($"cultivation-realm-{realm}"),
                FontColorOverride = reached ? Gold : Locked,
                StyleClasses = { "LabelHeading" },
            });
            if (current)
                heading.AddChild(new Label { Text = Loc.GetString("qi-path-window-current"), FontColorOverride = Muted, VAlign = Label.VAlignMode.Center });
            section.AddChild(heading);

            // Techniques as fixed-size pills, three to a row, so long names never run into each other.
            var actions = byRealm.GetValueOrDefault(realm) ?? new List<EntProtoId>();
            BoxContainer? row = null;
            for (var k = 0; k < actions.Count; k++)
            {
                if (k % PillsPerRow == 0)
                {
                    row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6, Margin = new Thickness(6, 0, 0, 0) };
                    section.AddChild(row);
                }

                row!.AddChild(Pill(actions[k], reached, current));
            }

            _realms.AddChild(section);
        }
    }

    private const int PillsPerRow = 3;
    private const float PillWidth = 156f;
    private const float PillHeight = 24f;

    private static void Add(Dictionary<CultivationRealm, List<EntProtoId>> byRealm, CultivationRealm realm, EntProtoId action)
    {
        if (!byRealm.TryGetValue(realm, out var list))
            byRealm[realm] = list = new List<EntProtoId>();

        if (!list.Contains(action))
            list.Add(action);
    }

    private Control Pill(EntProtoId action, bool unlocked, bool current)
    {
        var name = _prototypeManager.TryIndex(action, out var prototype) ? prototype.Name : action.Id;
        var pill = new PanelContainer
        {
            SetSize = new Vector2(PillWidth, PillHeight),
            MouseFilter = MouseFilterMode.Stop,
            ToolTip = prototype?.Description ?? string.Empty,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = unlocked ? Color.FromHex("#2a2d33") : Color.FromHex("#1c1d21"),
                BorderColor = unlocked ? (current ? Gold : Gold.WithAlpha(0.45f)) : Color.FromHex("#303236"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
            },
        };
        pill.AddChild(new Label
        {
            Text = name,
            ClipText = true,
            HorizontalExpand = true,
            VAlign = Label.VAlignMode.Center,
            FontColorOverride = unlocked ? Color.White : Locked,
        });
        return pill;
    }
}
