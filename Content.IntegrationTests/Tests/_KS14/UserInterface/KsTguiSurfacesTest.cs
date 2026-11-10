using System.Threading.Tasks;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     Surfaces outside windows that take their colours from the theme: radial menu sectors, which draw them in code.
/// </summary>
[TestOf(typeof(RadialMenuButtonWithSector))]
public sealed class KsTguiSurfacesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private (Color Normal, Color Hover) ReadColours(RadialMenuButtonWithSector button)
    {
        UiManager.PopupRoot.AddChild(button);
        button.ForceRunStyleUpdate();
        var colours = (button.KsResolveBackground(hovered: false), button.KsResolveBackground(hovered: true));
        button.Orphan();
        return colours;
    }

    [Test]
    public async Task SectorColoursFollowTheTheme()
    {
        (Color Normal, Color Hover) baron = default;
        (Color Normal, Color Hover) tgui = default;

        await SetTheme(false);
        await Client.WaitPost(() => baron = ReadColours(new RadialMenuButtonWithSector()));

        await SetTheme(true);
        await Client.WaitPost(() => tgui = ReadColours(new RadialMenuButtonWithSector()));

        Assert.Multiple(() =>
        {
            Assert.That(tgui.Normal, Is.Not.EqualTo(baron.Normal), "The ring should change with the theme");
            Assert.That(tgui.Hover, Is.Not.EqualTo(baron.Hover), "The hover should change with the theme");
            Assert.That(tgui.Hover, Is.Not.EqualTo(tgui.Normal), "Hover should still read");
        });
    }

    [Test]
    public async Task ColourSetInCodeBeatsTheTheme()
    {
        await SetTheme(true);

        var wanted = new Color(0.2f, 0.4f, 0.6f, 0.5f);
        (Color Normal, Color Hover) colours = default;
        await Client.WaitPost(() => colours = ReadColours(new RadialMenuButtonWithSector { BackgroundColor = wanted }));

        Assert.That(colours.Normal.R, Is.EqualTo(wanted.R).Within(0.01f));
        Assert.That(colours.Normal.B, Is.EqualTo(wanted.B).Within(0.01f));
    }

    [Test]
    public async Task ProgressColoursFollowTheTheme()
    {
        var progressColor = CEntMan.System<ProgressColorSystem>();
        Color baronHalf = default, tguiHalf = default;

        await SetTheme(false);
        await Client.WaitPost(() => baronHalf = progressColor.GetProgressColor(0.5f));
        await SetTheme(true);
        await Client.WaitPost(() => tguiHalf = progressColor.GetProgressColor(0.5f));

        Assert.That(tguiHalf, Is.Not.EqualTo(baronHalf));
    }
}
