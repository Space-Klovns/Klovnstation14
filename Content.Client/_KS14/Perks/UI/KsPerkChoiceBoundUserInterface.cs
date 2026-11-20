using Content.Shared._KS14.Perks;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Perks.UI;

/// <summary>
///     The perk picker on the player's own body. It can't be closed from here: it goes when the server takes the
///         choice and closes the interface.
/// </summary>
[UsedImplicitly]
public sealed partial class KsPerkChoiceBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private KsPerkChoiceMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<KsPerkChoiceMenu>();
        Update();
    }

    public override void Update()
    {
        if (_menu == null || !EntMan.TryGetComponent<KsPerkChoiceComponent>(Owner, out var choiceComponent))
            return;

        var spriteSystem = EntMan.System<SpriteSystem>();
        var options = new List<(SpriteSpecifier, string, Action)>();
        foreach (var perk in choiceComponent.Options)
        {
            if (!_prototypeManager.TryIndex(perk, out var perkPrototype))
                continue;

            var toolTip = $"{Loc.GetString(perkPrototype.Name)}\n{Loc.GetString(perkPrototype.Description)}";
            options.Add((perkPrototype.Icon, toolTip, () => SendMessage(new KsPerkChoiceSelectMessage(perk))));
        }

        _menu.SetOptions(options, icon => spriteSystem.Frame0(icon));
    }

    protected override void Dispose(bool disposing)
    {
        // The server closed it: let the window go with the interface.
        if (_menu != null)
            _menu.Locked = false;

        base.Dispose(disposing);
    }
}
