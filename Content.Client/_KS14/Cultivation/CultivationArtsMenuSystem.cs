using System.Linq;
using Content.Client.Actions;
using Content.Client.Stylesheets.Palette;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Actions.Components;
using Content.Shared.Popups;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Cultivation;

/// <summary>
///     The four category menus. Pressing Offensive, Defensive, Movement or Utility Arts opens a radial menu at the mouse
///         of every art of that kind the player knows, on the bar or not; picking one uses it at once (or starts aiming).
///         An art that can't be used right now (not enough qi, cooling down) is shown dimmed. The Utility menu also
///         holds the Favourites menu, for choosing which arts sit on the bar.
/// </summary>
public sealed partial class CultivationArtsMenuSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    [Dependency] private ActionsSystem _actionsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    [Dependency] private Robust.Client.Graphics.IOverlayManager _overlayManager = default!;

    private SimpleRadialMenu? _menu;
    private Overlays.KsAimRangeOverlay? _aimOverlay;

    public override void Initialize()
    {
        base.Initialize();
        _aimOverlay = new Overlays.KsAimRangeOverlay();
        _overlayManager.AddOverlay(_aimOverlay);
    }

    [SubscribeLocalEvent]
    private void OnArtsMenu(Entity<CultivationLoadoutComponent> entity, ref CultivationArtsMenuActionEvent args)
    {
        if (args.Handled || entity.Owner != _playerManager.LocalEntity || !_gameTiming.IsFirstTimePredicted)
            return;

        args.Handled = true;
        CloseMenu();

        var buttons = new List<RadialMenuOptionBase>();
        var now = _gameTiming.CurTime;
        var category = args.Category;
        var arts = _actionsSystem.GetClientActions()
            .Where(action => CompOrNull<CultivationArtCategoryComponent>(action)?.Category == category)
            .OrderBy(action => action.Comp.Priority)
            .ThenBy(action => Name(action));

        foreach (var action in arts)
        {
            var metaData = MetaData(action);
            var cooling = action.Comp.Cooldown is { } cooldown && cooldown.End > now;
            var usable = action.Comp.Enabled && !cooling;
            var tip = cooling && action.Comp.Cooldown is { } cd
                ? Loc.GetString("cultivation-arts-menu-cooling", ("technique", metaData.EntityName),
                    ("seconds", (int)Math.Ceiling((cd.End - now).TotalSeconds)), ("description", metaData.EntityDescription))
                : Loc.GetString("cultivation-arts-menu-tip", ("technique", metaData.EntityName), ("description", metaData.EntityDescription));
            if (action.Comp.UseDelay is { } useDelay && useDelay > TimeSpan.Zero)
                tip += "\n" + Loc.GetString("ks-action-tooltip-cooldown-plain",
                    ("seconds", Content.Client.UserInterface.Systems.Actions.Controls.ActionButton.KsCooldownText(useDelay)));

            buttons.Add(new RadialMenuActionOption<EntityUid>(Use, action.Owner)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(action.Comp.Icon),
                ToolTip = tip,
                BackgroundColor = usable ? null : Color.Black.WithAlpha(170),
            });
        }

        // The Favourites menu lives in Utility.
        if (args.Category == CultivationArtCategory.Utility && entity.Comp.MenuAction is { } menuAction
            && TryComp<ActionComponent>(menuAction, out var menuActionComponent))
        {
            buttons.Add(new RadialMenuActionOption<EntityUid>(Use, menuAction)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(menuActionComponent.Icon),
                ToolTip = Loc.GetString("cultivation-arts-menu-favourites"),
                BackgroundColor = Palettes.Gold.Element.WithAlpha(128),
            });
        }

        if (buttons.Count == 0)
        {
            _popupSystem.PopupClient(Loc.GetString("cultivation-arts-menu-empty"), entity, entity, PopupType.Small);
            return;
        }

        _menu = new SimpleRadialMenu();
        _menu.SetButtons(buttons);
        _menu.OnClose += OnMenuClosed;
        _menu.Open();
        _menu.OpenOverMouseScreenPosition();
    }

    private void Use(EntityUid actionUid)
    {
        CloseMenu();
        _uiManager.GetUIController<ActionUIController>().KsUseAction(actionUid);
    }

    private void OnMenuClosed()
    {
        _menu = null;
    }

    private void CloseMenu()
    {
        if (_menu == null)
            return;

        var menu = _menu;
        _menu = null;
        menu.OnClose -= OnMenuClosed;
        menu.Close();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CloseMenu();
        if (_aimOverlay != null)
            _overlayManager.RemoveOverlay(_aimOverlay);
    }
}
