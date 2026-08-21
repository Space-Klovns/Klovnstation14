using Content.Client.Stylesheets.Palette;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Anchorless.Components;
using Content.Shared._KS14.Anchorless.Systems;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Anchorless.UI;

/// <summary>The Anchorless counterpart to the Changeling identity radial.</summary>
[UsedImplicitly]
public sealed class AnchorlessTransformBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
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
        if (_menu == null || !EntMan.TryGetComponent<KsAnchorlessAntagComponent>(Owner, out var identity))
            return;

        var buttons = new List<RadialMenuOptionBase>();
        foreach (var stored in identity.LearnedIdentities)
        {
            if (stored.StoredIdentity == null)
                continue;

            buttons.Add(new RadialMenuActionOption<NetEntity>(Select, EntMan.GetNetEntity(stored.StoredIdentity.Value))
            {
                IconSpecifier = RadialMenuIconSpecifier.With(stored.StoredIdentity.Value),
                ToolTip = stored.OriginalName,
                BackgroundColor = identity.CurrentIdentity == stored.StoredIdentity
                    ? Palettes.Green.Element.WithAlpha(128)
                    : null,
            });
        }

        _menu.SetButtons(buttons);
    }

    private void Select(NetEntity identity)
        => SendPredictedMessage(new AnchorlessTransformIdentitySelectMessage(identity));
}
