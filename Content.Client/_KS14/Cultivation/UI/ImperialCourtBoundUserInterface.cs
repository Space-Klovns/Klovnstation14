using Content.Client.Stylesheets.Palette;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Cultivation;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Cultivation.UI;

/// <summary>
///     The Imperial Court: every power of the Mandate in one radial menu. Powers used on someone open a second ring of
///         the people nearby; a power still cooling down is dimmed and says when it's ready.
/// </summary>
[UsedImplicitly]
public sealed partial class ImperialCourtBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private IGameTiming _gameTiming = default!;

    private static readonly ResPath Icons = new("/Textures/_KS14/Cultivation/mandate.rsi");

    private static readonly Dictionary<MandatePower, string> IconStates = new()
    {
        { MandatePower.Decree, "imperial_decree" },
        { MandatePower.Kneel, "mandate_kneel" },
        { MandatePower.Treasury, "mandate_treasury" },
        { MandatePower.Pardon, "mandate_pardon" },
        { MandatePower.Jinyiwei, "mandate_jinyiwei" },
        { MandatePower.Fealty, "mandate_fealty" },
        { MandatePower.Ennoble, "mandate_ennoble" },
        { MandatePower.Punishment, "mandate_punishment" },
    };

    private SimpleRadialMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_menu == null || state is not ImperialCourtState court)
            return;

        var now = _gameTiming.CurTime;
        var buttons = new List<RadialMenuOptionBase>();
        foreach (var power in court.Powers)
        {
            var name = Loc.GetString($"mandate-power-{power.ToString().ToLowerInvariant()}");
            var icon = RadialMenuIconSpecifier.With(new SpriteSpecifier.Rsi(Icons, IconStates[power]));
            var cooling = court.ReadyAt.TryGetValue(power, out var ready) && now < ready;
            var description = Loc.GetString($"mandate-power-{power.ToString().ToLowerInvariant()}-desc");
            var tip = cooling
                ? Loc.GetString("mandate-power-ready-in", ("power", name), ("seconds", (int) Math.Ceiling((ready - now).TotalSeconds)))
                : Loc.GetString("mandate-power-tip", ("power", name), ("description", description));
            var background = cooling ? Palettes.Neutral.Element.WithAlpha(90) : (Color?) null;

            if (!MandatePowers.Targeted(power))
            {
                buttons.Add(new RadialMenuActionOption<MandatePower>(chosen => Send(chosen, null), power)
                {
                    IconSpecifier = icon,
                    ToolTip = tip,
                    BackgroundColor = background,
                });
                continue;
            }

            // Who to use it on: everyone near enough to name.
            var people = new List<RadialMenuOptionBase>();
            foreach (var person in court.People)
            {
                if (!EntMan.TryGetEntity(person, out var uid))
                    continue;

                people.Add(new RadialMenuActionOption<(MandatePower, NetEntity)>(pick => Send(pick.Item1, pick.Item2), (power, person))
                {
                    IconSpecifier = RadialMenuIconSpecifier.With(uid),
                    ToolTip = EntMan.GetComponent<MetaDataComponent>(uid.Value).EntityName,
                });
            }

            buttons.Add(new RadialMenuNestedLayerOption(people)
            {
                IconSpecifier = icon,
                ToolTip = people.Count == 0 ? Loc.GetString("mandate-power-no-one", ("power", name)) : tip,
                BackgroundColor = background,
            });
        }

        _menu.SetButtons(buttons);
    }

    private void Send(MandatePower power, NetEntity? target)
    {
        SendMessage(new ImperialCourtPowerMessage(power, target));
        Close();
    }
}
