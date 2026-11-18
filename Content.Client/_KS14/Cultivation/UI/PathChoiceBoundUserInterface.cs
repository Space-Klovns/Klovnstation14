using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Cultivation;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Cultivation.UI;

/// <summary>
///     The Fork in the Road: the path of qi (then one of the five elements) or the path of the body, each with a short
///         description on hover.
/// </summary>
[UsedImplicitly]
public sealed class PathChoiceBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly ResPath Manuals = new("/Textures/_KS14/Cultivation/manuals.rsi");

    private static readonly (CultivationPathOption Option, string State)[] Elements =
    {
        (CultivationPathOption.Metal, "manual_metal"),
        (CultivationPathOption.Wood, "manual_wood"),
        (CultivationPathOption.Water, "manual_water"),
        (CultivationPathOption.Fire, "manual_fire"),
        (CultivationPathOption.Earth, "manual_earth"),
    };

    private SimpleRadialMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SimpleRadialMenu>();

        var elements = new List<RadialMenuOptionBase>();
        foreach (var (option, state) in Elements)
        {
            var key = option.ToString().ToLowerInvariant();
            elements.Add(new RadialMenuActionOption<CultivationPathOption>(Choose, option)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(new SpriteSpecifier.Rsi(Manuals, state)),
                ToolTip = Loc.GetString($"ascension-path-{key}-tip"),
            });
        }

        _menu.SetButtons(new List<RadialMenuOptionBase>
        {
            new RadialMenuNestedLayerOption(elements)
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
