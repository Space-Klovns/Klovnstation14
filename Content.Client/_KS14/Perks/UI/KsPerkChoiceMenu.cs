using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Perks.UI;

/// <summary>
///     A radial menu with no way out: no close button, no click-away, and <see cref="Close"/> does nothing while
///         <see cref="Locked"/>. The window goes when its BUI does, once the server has the player's choice.
/// </summary>
public sealed class KsPerkChoiceMenu : RadialMenu
{
    private const float ButtonSize = 64f;
    private const float IconSize = 48f;
    private const int ContainerRadius = 100;

    private readonly RadialContainer _container;

    /// <summary>
    ///     While set, closing is refused, whether by escape, close-all or a click outside.
    /// </summary>
    public bool Locked = true;

    public KsPerkChoiceMenu()
    {
        MinSize = new Vector2(450f, 450f);
        HorizontalExpand = true;
        VerticalExpand = true;

        // The centre button would go back a layer or close; the outer area closes. Neither has a use here.
        ContextualButton.Visible = false;
        MenuOuterAreaButton.Visible = false;

        _container = new RadialContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            InitialRadius = ContainerRadius,
            ReserveSpaceForHiddenChildren = false,
        };
        AddChild(_container);
    }

    public override void Close()
    {
        if (Locked)
            return;

        base.Close();
    }

    /// <summary>
    ///     Replaces the buttons.
    /// </summary>
    public void SetOptions(IEnumerable<(SpriteSpecifier Icon, string ToolTip, Action OnPressed)> options, Func<SpriteSpecifier, Robust.Client.Graphics.Texture> getTexture)
    {
        _container.RemoveAllChildren();

        foreach (var (icon, toolTip, onPressed) in options)
        {
            var button = new RadialMenuButtonWithSector
            {
                SetSize = new Vector2(ButtonSize, ButtonSize),
                ToolTip = toolTip,
                DrawBorder = true,
            };

            var texture = getTexture(icon);
            button.AddChild(new TextureRect
            {
                Texture = texture,
                TextureScale = texture.Width <= 32 ? new Vector2(2f, 2f) : Vector2.One,
                SetSize = new Vector2(IconSize, IconSize),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                Stretch = TextureRect.StretchMode.KeepCentered,
            });

            button.OnPressed += _ => onPressed();
            _container.AddChild(button);
        }
    }
}
