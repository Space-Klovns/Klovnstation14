using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client.Stylesheets.Fonts;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The tgui font as the client loads it. A headless client has no system fonts, so this is always the bundled fallback:
///         nothing here exercises a real Verdana load.
/// </summary>
[TestOf(typeof(KsTguiFonts))]
public sealed class KsTguiFontTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     The theme restyles on the frame after the cvar changes.
    /// </summary>
    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    [TestCase(FontKind.Regular)]
    [TestCase(FontKind.Bold)]
    [TestCase(FontKind.Italic)]
    [TestCase(FontKind.BoldItalic)]
    public async Task BundledFontLoads(FontKind kind)
    {
        var fonts = Client.ResolveDependency<KsTguiFonts>();

        await Client.WaitAssertion(() =>
        {
            Assert.That(fonts.UsingSystemVerdana, Is.False);

            var font = (StackedFont)fonts.GetFont(13, kind);

            // FontTag reads a size from a VectorFont at the top of a stack, and only there.
            Assert.That(font.Stack[0], Is.TypeOf<VectorFont>());
            Assert.That(((VectorFont)font.Stack[0]).Size, Is.EqualTo(13));
            Assert.That(font.GetHeight(1f), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task HijackFollowsTheToggle()
    {
        var hijackHolder = Client.ResolveDependency<FontTagHijackHolder>();

        await Client.WaitAssertion(() =>
        {
            Assert.That(hijackHolder.Hijack, Is.Not.Null, "It has to be installed at boot, not on the first toggle");
            Assert.That(hijackHolder.Hijack!(BoldTag.BoldFont, 12), Is.Not.Null);
            Assert.That(hijackHolder.Hijack!(FontTag.DefaultFont, 12), Is.Not.Null);
            Assert.That(hijackHolder.Hijack!(ItalicTag.ItalicFont, 12), Is.Not.Null);
            Assert.That(hijackHolder.Hijack!(BoldItalicTag.BoldItalicFont, 12), Is.Not.Null);
        });

        await SetTheme(false);

        await Client.WaitAssertion(() =>
            Assert.That(hijackHolder.Hijack!(BoldTag.BoldFont, 12), Is.Null, "Off should leave Baron's font prototypes"));
    }

    /// <summary>
    ///     A nested stack would silently turn every <c>[bold size=16]</c> back into 12.
    /// </summary>
    /// <summary>
    ///     The engine's sizes are points, and 12 of them draw at 16px, a third bigger than tgui's text.
    /// </summary>
    [Test]
    public async Task SizesArePixelsAndGlyphsStillDraw()
    {
        var fonts = Client.ResolveDependency<KsTguiFonts>();

        await Client.WaitAssertion(() =>
        {
            var font = (StackedFont)fonts.GetFont(12);

            Assert.That(((VectorFont)font._main).Size, Is.EqualTo(9));
            Assert.That(font.GetHeight(1f), Is.GreaterThan(0), "Metrics have to come from the real font, not the empty one");
            Assert.That(font.GetCharMetrics(new System.Text.Rune('A'), 1f), Is.Not.Null, "Glyphs have to fall through the empty font");
            Assert.That(font.Stack[0].GetCharMetrics(new System.Text.Rune('A'), 1f, fallback: false), Is.Null, "The size carrier must have no glyphs");
        });
    }

    [Test]
    public async Task MarkupSizesSurvive()
    {
        var hijackHolder = Client.ResolveDependency<FontTagHijackHolder>();

        await Client.WaitAssertion(() =>
        {
            var fontStack = new Stack<Font>();
            fontStack.Push(hijackHolder.Hijack!(FontTag.DefaultFont, 16));

            var size = FontTag.GetSizeForFontTag(fontStack, new MarkupNode("bold", null, null));

            Assert.That(size, Is.EqualTo(16));
        });
    }
}
