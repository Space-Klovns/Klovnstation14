using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The display components checked against tgui-core's own numbers (ProgressBar.scss, LabeledList.scss, Table.scss,
///         RoundGauge.scss, NoticeBox.scss and the rest): transitions, the label colon, baselines, the row pitch and so on.
///         Nothing headless draws, so this reads sizes, positions, values and state.
/// </summary>
[TestOf(typeof(KsTguiProgressBar))]
public sealed class KsTguiDisplayTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static void Lay(Control control, float width)
    {
        control.Measure(new Vector2(width, 2000f));
        control.Arrange(new UIBox2(0f, 0f, width, control.DesiredSize.Y));
    }

    private static Control ValueOf(KsTguiLabeledListItem item)
    {
        // The value box is the second child of a row.
        return item.Children.ElementAt(1);
    }

    private static Label LabelOf(KsTguiLabeledListItem item)
    {
        return (Label)item.Children.First();
    }

    #region ProgressBar

    /// <summary>
    ///     ProgressBar.scss: the fill's width and colour take a second (ease), the border's colour a second (ease-out), each
    ///         from where it had got to. A bar's first value is drawn in place.
    /// </summary>
    [Test]
    public async Task ProgressBarFillAndColoursTransitionOverASecond()
    {
        await Client.WaitPost(() =>
        {
            var bar = new KsTguiProgressBar { Value = 0.2f };
            bar.AddRange(KsTguiColor.Bad, 0f, 0.5f);
            bar.AddRange(KsTguiColor.Good, 0.5f, 1f);
            Assert.That(bar.DisplayedFraction, Is.EqualTo(0.2f), "The first value is drawn in place.");
            Assert.That(bar.Transitioning, Is.False);

            bar.Advance(0f);
            var bad = bar.DisplayedFillColor;
            bar.Value = 0.8f;
            Assert.That(bar.DisplayedFraction, Is.EqualTo(0.2f), "Nothing has passed yet.");
            Assert.That(bar.Transitioning, Is.True);

            bar.Advance(0.5f);
            var fillProgress = KsTguiProgressBar.EaseDefault(0.5f);
            var borderProgress = KsTguiProgressBar.EaseOut(0.5f);
            Assert.That(fillProgress, Is.Not.EqualTo(borderProgress).Within(0.05f), "ease and ease-out are different curves");
            Assert.That(bar.DisplayedFraction, Is.EqualTo(0.2f + 0.6f * fillProgress).Within(0.001f));

            var good = KsTguiColors.Element(KsTguiColor.Good);
            Assert.That(bar.DisplayedFillColor.G, Is.EqualTo(bad.G + (good.G - bad.G) * fillProgress).Within(0.001f), "The fill colour eases on ease.");
            Assert.That(bar.DisplayedBorderColor.G, Is.EqualTo(bad.G + (good.G - bad.G) * borderProgress).Within(0.001f), "The border colour eases on ease-out.");

            // A new target midway starts from where it is.
            var midway = bar.DisplayedFraction;
            bar.Value = 0.2f;
            Assert.That(bar.DisplayedFraction, Is.EqualTo(midway).Within(0.0001f));

            bar.Advance(1.2f);
            Assert.That(bar.DisplayedFraction, Is.EqualTo(0.2f));
            Assert.That(bar.DisplayedFillColor, Is.EqualTo(KsTguiColors.Element(KsTguiColor.Bad)));
            Assert.That(bar.Transitioning, Is.False, "A settled bar has nothing left to do each frame.");
        });
    }

    /// <summary>
    ///     ProgressBar.js: only the width is clamped; the text is <c>(100 * scale).toFixed(fractionDigits)</c>.
    /// </summary>
    [Test]
    public async Task ProgressBarTextIsNotClampedAndTakesFractionDigits()
    {
        await Client.WaitPost(() =>
        {
            var bar = new KsTguiProgressBar { Value = 1.5f };
            Assert.That(bar.ShownText, Is.EqualTo("150%"));
            Assert.That(bar.Fraction, Is.EqualTo(1f), "the width is clamped");

            bar.Value = -0.2f;
            Assert.That(bar.ShownText, Is.EqualTo("-20%"));
            Assert.That(bar.Fraction, Is.EqualTo(0f));

            bar.FractionDigits = 1;
            bar.Value = 0.125f;
            Assert.That(bar.ShownText, Is.EqualTo("12.5%"));
        });
    }

    /// <summary>
    ///     ProgressBar.scss: the content box starts inside the 1px border and the 6px of padding, 7px from the edge.
    /// </summary>
    [Test]
    public async Task ProgressBarTextIsInsetByBorderAndPadding()
    {
        await Client.WaitPost(() =>
        {
            var bar = new KsTguiProgressBar { Value = 0.5f };
            bar.Measure(new Vector2(200f, 100f));
            bar.Arrange(new UIBox2(0f, 0f, 200f, 20f));

            var label = (Label)bar.Children.First();
            Assert.That(label.Position.X, Is.EqualTo(7f).Within(0.01f));
            Assert.That(label.Position.X + label.Size.X, Is.EqualTo(193f).Within(0.01f));
        });
    }

    #endregion

    #region LabeledList

    /// <summary>
    ///     LabeledList.js appends a colon to a label that is a string. One already written is left alone.
    /// </summary>
    [Test]
    public async Task LabeledListAddsTheColon()
    {
        await Client.WaitPost(() =>
        {
            var list = new KsTguiLabeledList();
            Assert.That(list.AddItem("Pressure").ShownLabel, Is.EqualTo("Pressure:"));
            Assert.That(list.AddItem("Pressure:").ShownLabel, Is.EqualTo("Pressure:"), "not doubled");
            Assert.That(list.AddItem(string.Empty).ShownLabel, Is.Null.Or.Empty, "an empty label gets none");

            var plain = list.AddItem("Name");
            plain.Colon = false;
            Assert.That(plain.ShownLabel, Is.EqualTo("Name"), "a label that isn't a string has none");
        });
    }

    /// <summary>
    ///     The value starts at <c>max(label + 12, 60)</c>: <c>min-width: 5em</c> is border-box, so it includes the cell's padding.
    ///         Buttons are right-aligned in their column.
    /// </summary>
    [Test]
    public async Task LabeledListColumnsAndButtons()
    {
        await Client.WaitPost(() =>
        {
            var list = new KsTguiLabeledList();
            var shortRow = list.AddItem("Id", "one");
            var wideRow = list.AddItem("A label wide enough to pass the minimum", "two");
            var wide = new KsTguiButton { Text = "A wide button" };
            var narrow = new KsTguiButton { Text = "Go" };
            wideRow.Buttons = wide;
            var narrowRow = list.AddItem("Id", "three");
            narrowRow.Buttons = narrow;
            Lay(list, 400f);

            var labelWidth = LabelOf(wideRow).DesiredSize.X;
            Assert.That(ValueOf(wideRow).Position.X, Is.EqualTo(labelWidth + 12f).Within(0.5f));
            Assert.That(ValueOf(shortRow).Position.X, Is.EqualTo(ValueOf(wideRow).Position.X).Within(0.01f), "columns line up");

            var shortOnly = new KsTguiLabeledList();
            var only = shortOnly.AddItem("Id", "one");
            Lay(shortOnly, 400f);
            Assert.That(ValueOf(only).Position.X, Is.EqualTo(60f).Within(0.01f), "the label column has a minimum of 5em, padding included");

            Assert.That(narrow.GlobalPosition.X + narrow.Size.X, Is.EqualTo(list.GlobalPosition.X + list.Size.X).Within(0.01f), "buttons sit at the right of their column");
            Assert.That(wide.GlobalPosition.X + wide.Size.X, Is.EqualTo(list.GlobalPosition.X + list.Size.X).Within(0.01f));
            Assert.That(narrow.GlobalPosition.X, Is.GreaterThan(wide.GlobalPosition.X), "the narrow set is right-aligned, not left");
        });
    }

    /// <summary>
    ///     Rows are 3px padding either side, so 6px apart; a divider row has no padding of its own, so the line sits
    ///         3 + 14 + 3 between two rows, and a size adds <c>(size - 1)</c> rem above and below.
    /// </summary>
    [Test]
    public async Task LabeledListRowPitchAndDividers()
    {
        await Client.WaitPost(() =>
        {
            float Gap(int? dividerSize)
            {
                var list = new KsTguiLabeledList();
                var first = list.AddItem("A", "one");
                if (dividerSize is { } size)
                    list.AddDivider(size);

                var second = list.AddItem("B", "two");
                Lay(list, 300f);
                return second.Position.Y - (first.Position.Y + first.Size.Y);
            }

            Assert.That(Gap(null), Is.EqualTo(6f).Within(0.01f));
            Assert.That(Gap(0), Is.EqualTo(20f).Within(0.01f));
            Assert.That(Gap(2), Is.EqualTo(44f).Within(0.01f));
        });
    }

    /// <summary>
    ///     td is baseline-aligned. A tall value pulls the text baseline down with it, so the label sits level with the
    ///         value's text rather than at the top; <c>verticalAlign</c> changes that.
    /// </summary>
    [Test]
    public async Task LabeledListCellsShareABaselineUnlessToldOtherwise()
    {
        await Client.WaitPost(() =>
        {
            var list = new KsTguiLabeledList();
            var row = list.AddItem("Label");
            var tall = new Control { MinSize = new Vector2(10f, 40f) };
            row.AddChild(tall);
            Lay(list, 300f);

            var label = LabelOf(row);
            Assert.That(label.Position.Y + label.Size.Y / 2f, Is.EqualTo(row.Size.Y / 2f).Within(1.5f), "level with the middle of a tall control");
            Assert.That(label.Position.Y, Is.GreaterThan(1f));

            row.VerticalAlign = KsTguiVerticalAlign.Top;
            Lay(list, 300f);
            Assert.That(label.Position.Y, Is.EqualTo(0f).Within(0.01f));

            row.VerticalAlign = KsTguiVerticalAlign.Bottom;
            Lay(list, 300f);
            Assert.That(label.Position.Y + label.Size.Y, Is.EqualTo(row.Size.Y).Within(0.01f));
        });
    }

    /// <summary>
    ///     <c>tooltip</c> puts the engine tooltip on the label, which must take the mouse to show it.
    /// </summary>
    [Test]
    public async Task LabeledListTooltipAndLabelWrap()
    {
        await Client.WaitPost(() =>
        {
            var list = new KsTguiLabeledList();
            var row = list.AddItem("Label");
            Assert.That(LabelOf(row).MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));

            row.Tooltip = "Hint";
            Assert.That(LabelOf(row).ToolTip, Is.EqualTo("Hint"));
            Assert.That(LabelOf(row).MouseFilter, Is.Not.EqualTo(Control.MouseFilterMode.Ignore));

            // A wrapping label takes no part in the column's width.
            var wrapped = list.AddItem("A very long label that would widen the whole column if it was not allowed to wrap");
            wrapped.LabelWrap = true;
            Lay(list, 300f);
            Assert.That(ValueOf(row).Position.X, Is.EqualTo(60f).Within(0.01f));
            Assert.That(wrapped.ShownLabel, Does.EndWith(":"));
        });
    }

    #endregion

    #region Table

    /// <summary>
    ///     Table.scss: header cells are bold. A label with a look of its own keeps it.
    /// </summary>
    [Test]
    public async Task TableHeaderCellsAreBold()
    {
        await Client.WaitPost(() =>
        {
            var header = new KsTguiTableRow { Header = true };
            var plain = new Label { Text = "Plain" };
            var styled = new Label { Text = "Styled" };
            styled.AddStyleClass("LabelHeading");
            header.AddChild(plain);
            header.AddChild(styled);

            var cell = new KsTguiTableCell { Header = true };
            var inCell = new Label { Text = "In a cell" };
            cell.AddChild(inCell);

            var body = new KsTguiTableRow();
            var bodyLabel = new Label { Text = "Body" };
            body.AddChild(bodyLabel);

            // A row whose header flag comes after its cells.
            var late = new KsTguiTableRow();
            var lateLabel = new Label { Text = "Late" };
            late.AddChild(lateLabel);
            late.Header = true;
            Assert.That(lateLabel.FontOverride, Is.Not.Null);

            Assert.That(plain.FontOverride, Is.Not.Null);
            Assert.That(styled.FontOverride, Is.Null);
            Assert.That(inCell.FontOverride, Is.Not.Null, "a header cell is bold too");
            Assert.That(bodyLabel.FontOverride, Is.Null);
        });
    }

    /// <summary>
    ///     A cell can span columns, a header cell has 6px under it, and a collapsing table is no wider than its columns.
    /// </summary>
    [Test]
    public async Task TableCellSpanHeaderAndCollapsing()
    {
        await Client.WaitPost(() =>
        {
            var table = new KsTguiTable();
            var top = new KsTguiTableRow();
            top.AddChild(new Label { Text = "A" });
            top.AddChild(new Label { Text = "BBBBBB" });
            top.AddChild(new Label { Text = "C" });
            table.AddChild(top);

            var spanRow = new KsTguiTableRow();
            var spanning = new KsTguiTableCell { ColSpan = 3 };
            spanning.AddChild(new Label { Text = "Spans all three" });
            spanRow.AddChild(spanning);
            table.AddChild(spanRow);

            var headRow = new KsTguiTableRow();
            var headCell = new KsTguiTableCell { Header = true };
            headCell.AddChild(new Label { Text = "Head" });
            headRow.AddChild(headCell);
            headRow.AddChild(new Label { Text = "x" });
            table.AddChild(headRow);

            Lay(table, 400f);
            Assert.That(spanning.Size.X, Is.EqualTo(table.Size.X).Within(0.5f), "it covers the three columns and the padding between them");
            Assert.That(spanning.Size.X, Is.GreaterThan(top.Children.Last().Size.X * 3f));

            var plain = (Label)top.Children.First();
            Assert.That(headRow.Size.Y, Is.EqualTo(plain.DesiredSize.Y + 6f).Within(0.5f), "a header cell has 6px under it");

            table.Collapsing = true;
            Lay(table, 400f);
            Assert.That(top.Size.X, Is.LessThan(399f), "no longer stretched to the width it was offered");
        });
    }

    /// <summary>
    ///     td is baseline-aligned: a short label next to a tall control sits level with its middle, not at the top.
    /// </summary>
    [Test]
    public async Task TableCellsShareABaseline()
    {
        await Client.WaitPost(() =>
        {
            var table = new KsTguiTable();
            var row = new KsTguiTableRow();
            var label = new Label { Text = "Text" };
            row.AddChild(label);
            row.AddChild(new Control { MinSize = new Vector2(10f, 40f) });
            table.AddChild(row);
            Lay(table, 300f);

            Assert.That(label.Position.Y + label.Size.Y / 2f, Is.EqualTo(row.Size.Y / 2f).Within(1.5f));
            Assert.That(label.Position.Y, Is.GreaterThan(1f));
        });
    }

    #endregion

    #region NoticeBox, BlockQuote, ColorBox, Modal

    /// <summary>
    ///     NoticeBox.scss makes a box of every colour name, black text on yellow and white. Under the tgui theme each colour
    ///         has its own panel.
    /// </summary>
    [Test]
    public async Task NoticeBoxHasEveryColour()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var notices = new List<(KsTguiNoticeColor Color, KsTguiNoticeBox Box)>();
        DefaultWindow window = null;
        await Client.WaitPost(() =>
        {
            var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            foreach (var color in Enum.GetValues<KsTguiNoticeColor>())
            {
                var box = new KsTguiNoticeBox { Text = color.ToString(), NoticeColor = color };
                notices.Add((color, box));
                root.AddChild(box);
            }

            window = new DefaultWindow { Title = "Notices" };
            window.Contents.AddChild(root);
            window.OpenCentered();
        });

        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            foreach (var (color, box) in notices)
            {
                box.ForceRunStyleUpdate();
                Assert.That(box.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var panel), Is.True, $"{color} has no panel");
                Assert.That(panel, Is.TypeOf<KsTguiNoticeStyleBox>(), color.ToString());
                Assert.That(((KsTguiNoticeStyleBox)panel).BackgroundColor, Is.Not.EqualTo(default(Color)), color.ToString());
            }

            var yellow = KsTguiNoticeBox.BackgroundOf(KsTguiNoticeColor.Yellow, default);
            Assert.That(yellow.R, Is.EqualTo(0xC0 / 255f).Within(2f / 255f));
            Assert.That(yellow.G, Is.EqualTo(0xA3 / 255f).Within(2f / 255f));
            Assert.That(yellow.B, Is.EqualTo(0x2C / 255f).Within(2f / 255f));

            Assert.That(KsTguiNoticeBox.TextColor(KsTguiNoticeColor.Yellow), Is.EqualTo(Color.Black));
            Assert.That(KsTguiNoticeBox.TextColor(KsTguiNoticeColor.White), Is.EqualTo(Color.Black));
            Assert.That(KsTguiNoticeBox.TextColor(KsTguiNoticeColor.Danger), Is.EqualTo(Color.White));

            var spaced = new KsTguiNoticeBox();
            Assert.That(spaced.Margin.Bottom, Is.EqualTo(0f));
            spaced.Spaced = true;
            Assert.That(spaced.Margin.Bottom, Is.EqualTo(6f), "tgui's margin-bottom");

            window.Close();
        });
    }

    /// <summary>
    ///     BlockQuote.scss: the 6px below is a margin outside the bar, so the bar ends with the text. A last child has none.
    /// </summary>
    [Test]
    public async Task BlockQuoteBarEndsWithTheText()
    {
        await Client.WaitPost(() =>
        {
            var quote = new KsTguiBlockQuote { Text = "Quoted" };
            Assert.That(quote.Margin.Bottom, Is.EqualTo(6f));

            var panel = (StyleBoxFlat)quote.PanelOverride;
            Assert.That(panel.GetContentMargin(StyleBox.Margin.Bottom), Is.EqualTo(0f), "the bar must not be drawn 6px past the text");
            Assert.That(panel.GetContentMargin(StyleBox.Margin.Left), Is.EqualTo(8f), "2px bar and 6px of padding");

            quote.Spaced = false;
            Assert.That(quote.Margin.Bottom, Is.EqualTo(0f));
        });
    }

    /// <summary>
    ///     ColorBox: no colour is transparent, a name is its background shade, and <c>content</c> is drawn on it.
    /// </summary>
    [Test]
    public async Task ColorBoxDefaultsToTransparentAndShowsContent()
    {
        await Client.WaitPost(() =>
        {
            var box = new KsTguiColorBox();
            Assert.That(box.BoxColor.A, Is.EqualTo(0f));

            box.NamedColor = KsTguiColor.Red;
            Assert.That(box.BoxColor, Is.EqualTo(KsTguiColors.Element(KsTguiColor.Red)));

            box.Content = "X";
            Lay(box, 100f);
            var label = (Label)box.Children.First();
            Assert.That(label.Visible, Is.True);
            Assert.That(label.Position.X + label.Size.X / 2f, Is.EqualTo(6f).Within(0.5f), "centred on the 12px box");
            Assert.That(box.DesiredSize, Is.EqualTo(new Vector2(12f, 12f)));
        });
    }

    /// <summary>
    ///     Modal.js: Enter calls <c>onEnter</c> and does not close the modal.
    /// </summary>
    [Test]
    public async Task ModalEnterRaisesEnteredWithoutClosing()
    {
        await Client.WaitPost(() =>
        {
            var modal = new KsTguiModal { Body = new Label { Text = "Form" } };
            var entered = 0;
            Assert.That(modal.HandleKeyFunction(EngineKeyFunctions.TextSubmit), Is.False, "no one listening, so it isn't taken");

            modal.Entered += () => entered++;
            modal.Open();
            Assert.That(modal.HandleKeyFunction(EngineKeyFunctions.TextSubmit), Is.True);
            Assert.That(entered, Is.EqualTo(1));
            Assert.That(modal.IsOpen, Is.True, "tgui's modal leaves closing to onEnter");

            modal.Close();
            Assert.That(modal.HandleKeyFunction(EngineKeyFunctions.TextSubmit), Is.False, "a closed one ignores it");
        });
    }

    #endregion

    #region RoundGauge

    /// <summary>
    ///     RoundGauge.scss: a range is drawn 200 degrees of arc per unit (dasharray arithmetic), the track is the last 160, the
    ///         ring is primary without ranges, the idle colours are sRGB-blended, and the text gap is an em of the gauge's size.
    /// </summary>
    [Test]
    public async Task RoundGaugeFollowsTheCss()
    {
        await Client.WaitPost(() =>
        {
            var (start, end) = KsTguiRoundGauge.RangeArc(0f, 50f, 0f, 100f);
            Assert.That(start, Is.EqualTo(0f));
            Assert.That(end, Is.EqualTo(0.5f * 200f / 180f).Within(0.0001f), "200 degrees per unit, not 180");

            (start, end) = KsTguiRoundGauge.RangeArc(70f, 100f, 0f, 100f);
            Assert.That(start, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(end, Is.EqualTo(1f), "what passes the right-hand end is clipped");

            var gauge = new KsTguiRoundGauge();
            Assert.That(gauge.MinValue, Is.EqualTo(1f), "tgui-core's default");
            Assert.That(gauge.DefaultRingColor.R, Is.EqualTo(0x48 / 255f).Within(2f / 255f));
            Assert.That(gauge.DefaultRingColor.G, Is.EqualTo(0x73 / 255f).Within(2f / 255f));
            Assert.That(gauge.DefaultRingColor.B, Is.EqualTo(0x9E / 255f).Within(2f / 255f));

            Assert.That(KsTguiRoundGauge.IdleColor, Is.Not.EqualTo(new Color(1f, 1f, 1f, 0.1f)), "white at .1 is not the raw alpha here");
            Assert.That(KsTguiRoundGauge.IdleColor, Is.EqualTo(KsTguiPalettes.Translucent(Color.White, 0.1f, KsTguiPalettes.Section)));

            gauge.GaugeSize = 1f;
            gauge.Measure(new Vector2(500f, 500f));
            var small = gauge.DesiredSize.Y;
            gauge.GaugeSize = 2f;
            gauge.Measure(new Vector2(500f, 500f));
            Assert.That(gauge.DesiredSize.Y - small, Is.EqualTo(15.6f + 2.4f).Within(0.01f), "the gap under the dial grows with the size");
        });
    }

    /// <summary>
    ///     The "!" is a hole in the triangle, so a point in it is covered by none of the glyph's pieces, a point beside it is,
    ///         and a point outside the triangle is not.
    /// </summary>
    [Test]
    public async Task RoundGaugeAlertGlyphHasAHole()
    {
        await Client.WaitPost(() =>
        {
            Assert.That(KsTguiRoundGauge.AlertGlyphCovers(new Vector2(50f, 25f)), Is.False, "the stem of the \"!\"");
            Assert.That(KsTguiRoundGauge.AlertGlyphCovers(new Vector2(50f, 37f)), Is.False, "its dot");
            Assert.That(KsTguiRoundGauge.AlertGlyphCovers(new Vector2(50f, 33.5f)), Is.True, "the gap between them");
            Assert.That(KsTguiRoundGauge.AlertGlyphCovers(new Vector2(42f, 38f)), Is.True, "the triangle beside it");
            Assert.That(KsTguiRoundGauge.AlertGlyphCovers(new Vector2(50f, 8f)), Is.False, "above the apex");
            Assert.That(KsTguiRoundGauge.AlertGlyphCovers(new Vector2(30f, 38f)), Is.False, "outside the triangle");
        });
    }

    #endregion
}
