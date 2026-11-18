using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Cultivation;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Cultivation.UI;

/// <summary>
///     The Fork in the Road: the path of qi or the path of the body, each with a short
///         description on hover.
/// </summary>
[UsedImplicitly]
public sealed class PathChoiceBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly ResPath Manuals = new("/Textures/_KS14/Cultivation/manuals.rsi");

    private SimpleRadialMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SimpleRadialMenu>();

        _menu.SetButtons(new List<RadialMenuOptionBase>
        {
            new RadialMenuActionOption<CultivationPathOption>(Choose, CultivationPathOption.Qi)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(new SpriteSpecifier.Rsi(Manuals, "manual_water")),
                ToolTip = Loc.GetString("ascension-path-qi-tip"),
            },
            new RadialMenuActionOption<CultivationPathOption>(Choose, CultivationPathOption.Body)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(new SpriteSpecifier.Rsi(Manuals, "body_molding_art")),
                ToolTip = Loc.GetString("ascension-path-body-tip"),
            },
        });
        _menu.OpenCentered();
    }

    private void Choose(CultivationPathOption option)
    {
        SendMessage(new PathChoiceMessage(option));
    }
}
