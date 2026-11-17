using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     SS14's <see cref="StripeBack"/> is drawn as tgui's notice box under the tgui theme, and like tg's
///         <c>NoticeBox color</c> it takes any of the palette's notice colours.
/// </summary>
[TestOf(typeof(StripeBack))]
public sealed class KsTguiStripeBackTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private StyleBox Background(StripeBack stripe)
    {
        stripe.ForceRunStyleUpdate();
        Assert.That(stripe.TryGetStyleProperty<StyleBox>(StripeBack.StylePropertyBackground, out var box));
        return box;
    }

    [Test]
    public async Task AStripeBackTakesTheNoticeColourItIsGiven()
    {
        await SetTheme(true);

        await Client.WaitPost(() =>
        {
            var plain = new StripeBack();
            var danger = new StripeBack { KsNoticeColor = KsTguiNoticeColor.Danger };
            var info = new StripeBack { KsNoticeColor = KsTguiNoticeColor.Info };
            var notice = new KsTguiNoticeBox { NoticeColor = KsTguiNoticeColor.Danger };
            UiManager.PopupRoot.AddChild(plain);
            UiManager.PopupRoot.AddChild(danger);
            UiManager.PopupRoot.AddChild(info);
            UiManager.PopupRoot.AddChild(notice);
            notice.ForceRunStyleUpdate();

            var plainBox = (KsTguiNoticeStyleBox)Background(plain);
            var dangerBox = (KsTguiNoticeStyleBox)Background(danger);
            var infoBox = (KsTguiNoticeStyleBox)Background(info);

            Assert.That(dangerBox.BackgroundColor, Is.EqualTo(KsTguiPalettes.NoticeBad), "Danger is tg's red notice.");
            Assert.That(infoBox.BackgroundColor, Is.EqualTo(KsTguiPalettes.NoticeInfo), "Info is tg's blue notice.");
            Assert.That(plainBox.BackgroundColor, Is.Not.EqualTo(dangerBox.BackgroundColor), "No colour is the default notice.");

            Assert.That(notice.TryGetStyleProperty<StyleBox>(Robust.Client.UserInterface.Controls.PanelContainer.StylePropertyPanel, out var noticeBox));
            Assert.That(dangerBox, Is.SameAs(noticeBox), "The same box as a notice box of that colour.");

            danger.KsNoticeColor = null;
            Assert.That(((KsTguiNoticeStyleBox)Background(danger)).BackgroundColor, Is.EqualTo(plainBox.BackgroundColor), "Taking the colour off goes back to the default.");

            plain.Orphan();
            danger.Orphan();
            info.Orphan();
            notice.Orphan();
        });
    }

    [Test]
    public async Task BaronIgnoresTheColour()
    {
        await SetTheme(false);

        await Client.WaitPost(() =>
        {
            var plain = new StripeBack();
            var danger = new StripeBack { KsNoticeColor = KsTguiNoticeColor.Danger };
            UiManager.PopupRoot.AddChild(plain);
            UiManager.PopupRoot.AddChild(danger);

            Assert.That(Background(danger), Is.SameAs(Background(plain)), "Baron has one stripe back.");
            Assert.That(Background(danger), Is.Not.InstanceOf<KsTguiNoticeStyleBox>());

            plain.Orphan();
            danger.Orphan();
        });

        await SetTheme(true);
    }
}
