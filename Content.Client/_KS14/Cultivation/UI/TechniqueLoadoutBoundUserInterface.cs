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
        // Stays open, so several techniques can be equipped or put away in a row.
        _menu.CloseOnSelect = false;
        Update();
        _menu.OpenCentered();
    }

    public override void Update()
    {
        if (_menu == null || !EntMan.TryGetComponent<CultivationLoadoutComponent>(Owner, out var loadout))
            return;

        var buttons = new List<RadialMenuOptionBase>();
        // Always-ready actions first (Forge the Body, the Tribulation of Flesh): picking one puts it back on the bar.
        foreach (var actionUid in loadout.Pinned)
        {
            if (!EntMan.TryGetComponent<ActionComponent>(actionUid, out var pinnedAction))
                continue;

            var pinnedMeta = EntMan.GetComponent<MetaDataComponent>(actionUid);
            buttons.Add(new RadialMenuActionOption<NetEntity>(Toggle, EntMan.GetNetEntity(actionUid))
            {
                IconSpecifier = RadialMenuIconSpecifier.With(pinnedAction.Icon),
                ToolTip = Loc.GetString("cultivation-loadout-pinned", ("technique", pinnedMeta.EntityName), ("description", pinnedMeta.EntityDescription)),
                BackgroundColor = Palettes.Gold.Element.WithAlpha(128),
            });
        }

        foreach (var actionUid in loadout.Learned)
        {
            if (!EntMan.TryGetComponent<ActionComponent>(actionUid, out var action))
                continue;

            var equipped = loadout.Equipped.Contains(actionUid);
            var metaData = EntMan.GetComponent<MetaDataComponent>(actionUid);
            var name = metaData.EntityName;
            var description = metaData.EntityDescription;
            buttons.Add(new RadialMenuActionOption<NetEntity>(Toggle, EntMan.GetNetEntity(actionUid))
            {
                IconSpecifier = RadialMenuIconSpecifier.With(action.Icon),
                ToolTip = Loc.GetString(equipped ? "cultivation-loadout-equipped" : "cultivation-loadout-stored-tip",
                    ("technique", name), ("count", loadout.Equipped.Count), ("slots", loadout.Slots), ("description", description)),
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
