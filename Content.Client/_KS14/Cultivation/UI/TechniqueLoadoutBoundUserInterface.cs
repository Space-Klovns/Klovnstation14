using Content.Client.Stylesheets.Palette;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Actions.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Cultivation.UI;

/// <summary>
///     The Techniques menu: every technique a cultivator knows, the equipped ones lit green. A click equips or puts away.
/// </summary>
[UsedImplicitly]
public sealed class TechniqueLoadoutBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SimpleRadialMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SimpleRadialMenu>();
        Update();
        _menu.OpenOverMouseScreenPosition();
    }

    public override void Update()
    {
        if (_menu == null || !EntMan.TryGetComponent<CultivationLoadoutComponent>(Owner, out var loadout))
            return;

        var buttons = new List<RadialMenuOptionBase>();
        foreach (var actionUid in loadout.Learned)
        {
            if (!EntMan.TryGetComponent<ActionComponent>(actionUid, out var action))
                continue;

            var equipped = loadout.Equipped.Contains(actionUid);
            var name = EntMan.GetComponent<MetaDataComponent>(actionUid).EntityName;
            buttons.Add(new RadialMenuActionOption<NetEntity>(Toggle, EntMan.GetNetEntity(actionUid))
            {
                IconSpecifier = RadialMenuIconSpecifier.With(action.Icon),
                ToolTip = Loc.GetString(equipped ? "cultivation-loadout-equipped" : "cultivation-loadout-stored-tip",
                    ("technique", name), ("count", loadout.Equipped.Count), ("slots", loadout.Slots)),
                BackgroundColor = equipped ? Palettes.Green.Element.WithAlpha(128) : null,
            });
        }

        _menu.SetButtons(buttons);
    }

    private void Toggle(NetEntity action)
    {
        SendMessage(new TechniqueLoadoutToggleMessage(action));
    }
}
