using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
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
        MinSize = new Vector2(470, 380);
        SetSize = new Vector2(520, 520);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(10, 6), SeparationOverride = 6 };
        _header = new RichTextLabel();
        root.AddChild(_header);
        root.AddChild(new RichTextLabel { Text = Loc.GetString("qi-path-window-hint"), Modulate = Muted });

        _realms = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 12 };
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
            var section = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
            section.AddChild(new Label
            {
                Text = Loc.GetString($"cultivation-realm-{realm}"),
                FontColorOverride = reached ? Gold : Locked,
                StyleClasses = { "LabelSubText" },
            });

            var chips = new GridContainer { Columns = 3, HSeparationOverride = 12, VSeparationOverride = 2, Margin = new Thickness(12, 0, 0, 0) };
            foreach (var action in byRealm.GetValueOrDefault(realm) ?? new List<EntProtoId>())
                chips.AddChild(Chip(action, reached));

            if (chips.ChildCount > 0)
                section.AddChild(chips);

            _realms.AddChild(section);
        }
    }

    private static void Add(Dictionary<CultivationRealm, List<EntProtoId>> byRealm, CultivationRealm realm, EntProtoId action)
    {
        if (!byRealm.TryGetValue(realm, out var list))
            byRealm[realm] = list = new List<EntProtoId>();

        if (!list.Contains(action))
            list.Add(action);
    }

    private Control Chip(EntProtoId action, bool unlocked)
    {
        var name = _prototypeManager.TryIndex(action, out var prototype) ? prototype.Name : action.Id;
        return new Label
        {
            Text = name,
            MinWidth = 140,
            ClipText = true,
            MouseFilter = MouseFilterMode.Stop,
            ToolTip = prototype?.Description ?? string.Empty,
            FontColorOverride = unlocked ? Color.White : Locked,
        };
    }
}
