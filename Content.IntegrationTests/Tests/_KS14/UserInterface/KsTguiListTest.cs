using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The standard for lists under the tgui theme: <c>SpawnSearch.tsx</c>'s rows, which are
///         <c>&lt;Button className="candystripe" color="transparent" fluid selected={...}&gt;</c>. Covers the engine's
///         <see cref="ItemList"/> and the rows of <see cref="ListContainer"/>, which are made to look that way by the sheet
///         and the content around it, and <see cref="KsTguiList"/>, which is that list. Nothing headless draws on screen,
///         so the item list's box is drawn into a handle that records what it is given.
/// </summary>
[TestOf(typeof(KsTguiList))]
public sealed class KsTguiListTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private ItemList MakeItemList(int count)
    {
        var list = new ItemList { SelectMode = ItemList.ItemListSelectMode.Single };
        for (var i = 0; i < count; i++)
            list.AddItem($"Row {i}");

        UiManager.PopupRoot.AddChild(list);
        list.ForceRunStyleUpdate();
        return list;
    }

    /// <summary>
    ///     Where <see cref="ItemList"/> puts row <paramref name="index"/> when it draws: 20 tall, 2 under it.
    /// </summary>
    private static UIBox2 Region(int index) => UIBox2.FromDimensions(0f, index * 22f, 200f, 22f);

    /// <summary>
    ///     Draws a row as <see cref="ItemList"/> does: it gives the item its rectangle, then hands the same one to the box.
    /// </summary>
    private static RecordingHandle DrawRow(ItemList list, int index)
    {
        var box = (KsTguiItemListRowStyleBox)list.ActualItemBackground;
        list[index].Region = Region(index);

        var handle = new RecordingHandle { Transform = Matrix3x2.CreateTranslation(new Vector2(list.GlobalPixelPosition.X, list.GlobalPixelPosition.Y)) };
        box.Draw(handle, Region(index), 1f);
        return handle;
    }

    [Test]
    public async Task ItemListUnderTguiStripesTheOddRowsAndNotTheEvenOnes()
    {
        await SetTheme(true);

        ItemList list = null;
        await Client.WaitPost(() => list = MakeItemList(5));

        await Client.WaitAssertion(() =>
        {
            Assert.That(list.ActualItemBackground, Is.TypeOf<KsTguiItemListRowStyleBox>(), "the tgui sheet gives the list its row box");

            var stripe = KsTguiThemePalette.Nanotrasen.Candystripe;
            Assert.That(((KsTguiItemListRowStyleBox)list.ActualItemBackground).Colors.Stripe, Is.EqualTo(stripe),
                "--candystripe-odd: black at .25");

            for (var i = 0; i < 5; i++)
            {
                var handle = DrawRow(list, i);

                // CSS counts from one: nth-child(odd) is the first, third and fifth.
                Assert.That(handle.DrewPrimitive(stripe), Is.EqualTo(i % 2 == 0),
                    $"row {i} (CSS child {i + 1}) {(i % 2 == 0 ? "is" : "is not")} one of the dark rows");
            }
        });
    }

    [Test]
    public async Task ItemListSelectedRowHasTgGreenTextOnAClearBackground()
    {
        await SetTheme(true);

        ItemList list = null;
        await Client.WaitPost(() =>
        {
            list = MakeItemList(3);
            list[1].Selected = true;
        });

        await Client.WaitAssertion(() =>
        {
            var colors = ((KsTguiItemListRowStyleBox)list.ActualItemBackground).Colors;

            // --button-background-selected is --color-green, hsl(140, 70%, 40%), and a selected transparent Button's text is
            //      that at +--adjust-color (5) lightness.
            var hsl = Color.ToHsl(colors.TextSelected);
            Assert.That(hsl.X * 360f, Is.EqualTo(140f).Within(1f), "hue");
            Assert.That(hsl.Y, Is.EqualTo(0.70f).Within(0.01f), "saturation");
            Assert.That(hsl.Z, Is.EqualTo(0.45f).Within(0.01f), "--color-green's 40% lightness plus --adjust-color");

            var look = KsTguiItemListRows.Look(colors, list, 1, hovered: false);
            Assert.That(look.Text, Is.EqualTo(colors.TextSelected));
            Assert.That(look.Wash, Is.Null, "a selected transparent Button stays clear");

            var unselected = KsTguiItemListRows.Look(colors, list, 0, hovered: false);
            Assert.That(unselected.Text, Is.EqualTo(colors.Text), "--button-color-transparent");
            Assert.That(colors.Text.A, Is.LessThan(1f), "which is the text colour at .5");

            // Drawn: the green is what reaches the screen, in place of the list's own text, which the sheet made clear.
            var handle = DrawRow(list, 1);
            Assert.That(handle.TextureColors, Is.Not.Empty, "the box draws the row's text");
            Assert.That(handle.TextureColors, Is.All.EqualTo(colors.TextSelected));
            Assert.That(handle.Primitives, Is.Empty, "row 2 of 3 is not a dark row, and a selected one has no wash");
            Assert.That(list.ActualFontColor.A, Is.EqualTo(0f), "so the list's own text is invisible");
        });
    }

    [Test]
    public async Task ItemListHoverDisabledAndPlainRowsFollowTheTransparentButton()
    {
        await SetTheme(true);

        ItemList list = null;
        await Client.WaitPost(() =>
        {
            list = MakeItemList(3);
            list[2].Disabled = true;
        });

        await Client.WaitAssertion(() =>
        {
            var colors = ((KsTguiItemListRowStyleBox)list.ActualItemBackground).Colors;

            var hovered = KsTguiItemListRows.Look(colors, list, 0, hovered: true);
            Assert.That(hovered.Text, Is.EqualTo(Color.White), "hover turns the text solid");
            Assert.That(hovered.Wash, Is.EqualTo(colors.Hover), "and washes the row white at .1");
            Assert.That(colors.Hover, Is.EqualTo(KsTguiThemePalette.Nanotrasen.Border));

            var selectedHovered = KsTguiItemListRows.Look(colors, 0, selected: true, disabled: false, hovered: true);
            Assert.That(selectedHovered.Text, Is.EqualTo(colors.TextSelected), "hover leaves a selected row's text green");
            Assert.That(selectedHovered.Wash, Is.EqualTo(colors.SelectedHover));

            var disabled = KsTguiItemListRows.Look(colors, list, 2, hovered: true);
            Assert.That(disabled.Text, Is.EqualTo(colors.TextDisabled), "--color-bad");
            Assert.That(disabled.Wash, Is.EqualTo(colors.DisabledWash), "on --color-bad at .2, and no hover state of its own");

            var plain = KsTguiItemListRows.Look(colors, list, 1, hovered: false);
            Assert.That(plain.Wash, Is.Null);
            Assert.That(plain.Text, Is.EqualTo(colors.Text));

            // tgui rows are 20 tall with 2 below: the list measures them from the box's margins.
            var box = (KsTguiItemListRowStyleBox)list.ActualItemBackground;
            Assert.That(box.MinimumSize.Y + list.ActualFont.GetHeight(1f), Is.EqualTo(22f).Within(0.01f));
            Assert.That(box.MinimumSize.X, Is.EqualTo(12f), "--space-m either side");
        });
    }

    [Test]
    public async Task ItemListFindsItsOwnRowWhenAnotherListHasTheSameRectangle()
    {
        await SetTheme(true);

        ItemList first = null;
        ItemList second = null;
        await Client.WaitPost(() =>
        {
            var holder = new LayoutContainer { MinSize = new Vector2(300f, 300f) };
            first = new ItemList();
            second = new ItemList();
            for (var i = 0; i < 3; i++)
            {
                first.AddItem($"First {i}");
                second.AddItem($"Second {i}");
            }

            holder.AddChild(first);
            holder.AddChild(second);
            LayoutContainer.SetPosition(first, new Vector2(0f, 0f));
            LayoutContainer.SetPosition(second, new Vector2(0f, 150f));
            first.SetSize = new Vector2(200f, 100f);
            second.SetSize = new Vector2(200f, 100f);
            UiManager.PopupRoot.AddChild(holder);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(second.GlobalPixelPosition, Is.Not.EqualTo(first.GlobalPixelPosition));

            first[0].Region = Region(0);
            second[0].Region = Region(0);

            Assert.That(KsTguiItemListRows.TryFindRow(Region(0), new Vector2(first.GlobalPixelPosition.X, first.GlobalPixelPosition.Y), out var found, out _), Is.True);
            Assert.That(found, Is.SameAs(first), "two lists with a row of the same rectangle are told apart by where they are drawn");

            Assert.That(KsTguiItemListRows.TryFindRow(Region(0), new Vector2(second.GlobalPixelPosition.X, second.GlobalPixelPosition.Y), out found, out _), Is.True);
            Assert.That(found, Is.SameAs(second));
        });
    }

    [Test]
    public async Task ItemListUnderBaronIsUnchanged()
    {
        await SetTheme(false);

        ItemList list = null;
        await Client.WaitPost(() => list = MakeItemList(3));

        await Client.WaitAssertion(() =>
        {
            Assert.That(list.ActualItemBackground, Is.Not.TypeOf<KsTguiItemListRowStyleBox>());
            Assert.That(list.ActualSelectedItemBackground, Is.Not.TypeOf<KsTguiItemListRowStyleBox>());
            Assert.That(list.ActualFontColor.A, Is.EqualTo(1f), "Baron's list draws its own text");
        });
    }

    private sealed record NumberedRow(int Number) : ListData;

    private async Task<(DefaultWindow Window, ListContainer List)> ShowListContainer(int count)
    {
        DefaultWindow window = null;
        ListContainer list = null;

        await Client.WaitPost(() =>
        {
            window = new DefaultWindow { Title = "List" };
            list = new ListContainer { Toggle = true, Group = true };
            list.GenerateItem += (data, button) =>
            {
                button.AddStyleClass(ListContainer.StyleClassListContainerButton);
                button.AddChild(new Label { Text = $"Entry {((NumberedRow)data).Number}" });
            };

            window.Contents.AddChild(list);
            window.SetSize = new Vector2(300f, 300f);
            window.OpenCentered();

            list.PopulateList(Enumerable.Range(0, count).Select(number => (ListData)new NumberedRow(number)).ToList());
        });

        await Pair.RunTicksSync(5);
        return (window, list);
    }

    private static List<ListContainerButton> Rows(ListContainer list)
    {
        return list.Children.OfType<ListContainerButton>().OrderBy(button => button.Index).ToList();
    }

    [Test]
    public async Task ListContainerRowsUnderTgui()
    {
        await SetTheme(true);
        var (window, list) = await ShowListContainer(5);

        await Client.WaitAssertion(() =>
        {
            var rows = Rows(list);
            Assert.That(rows, Has.Count.EqualTo(5));

            Assert.That(list.TryGetStyleProperty<int>(ListContainer.StylePropertySeparation, out var separation), Is.True);
            Assert.That(separation, Is.EqualTo(2), "a fluid Button keeps its 2px margin-bottom, so rows are 2px apart");

            for (var i = 0; i < rows.Count; i++)
            {
                Assert.That(rows[i].KsHasStripe, Is.EqualTo(i % 2 == 0), $"row {i} is a dark row only when CSS would count it odd");
                Assert.That(rows[i].TryGetStyleProperty<Color>(ListContainerButton.KsStylePropertyStripe, out var stripe), Is.True);
                Assert.That(stripe, Is.EqualTo(KsTguiThemePalette.Nanotrasen.Candystripe));
            }

            // Unselected text is --button-color-transparent. The window is the first thing to run its style update, so the
            //      labels are asked to be sure of theirs.
            var label = (Label)rows[0].GetChild(rows[0].ChildCount - 1);
            label.ForceRunStyleUpdate();
            Assert.That(label.TryGetStyleProperty<Color>(Label.StylePropertyFontColor, out var normal), Is.True);
            Assert.That(normal.A, Is.LessThan(1f), "the text colour at .5");
            window.Close();
        });
    }

    [Test]
    public async Task ListContainerSelectedRowHasGreenTextAndNoWash()
    {
        await SetTheme(true);
        var (window, list) = await ShowListContainer(4);

        await Client.WaitAssertion(() =>
        {
            list.Select(list.Data[1]);
            var rows = Rows(list);

            Assert.That(rows[1].HasStyleClass(ListContainerButton.KsStyleClassSelected), Is.True);
            Assert.That(rows[0].HasStyleClass(ListContainerButton.KsStyleClassSelected), Is.False);

            foreach (var row in rows)
                row.ForceRunStyleUpdate();

            var selected = (Label)rows[1].GetChild(rows[1].ChildCount - 1);
            selected.ForceRunStyleUpdate();
            var green = KsTguiThemePalette.Nanotrasen.SelectedButton.Hovered;
            Assert.That(selected.TryGetStyleProperty<Color>(Label.StylePropertyFontColor, out var text), Is.True);
            Assert.That(text, Is.EqualTo(green), "--button-background-selected at +5 lightness");

            Assert.That(rows[1].TryGetStyleProperty<Color>(Control.StylePropertyModulateSelf, out var wash), Is.True);
            Assert.That(wash.A, Is.EqualTo(0f), "and the row stays clear, the stripe still showing");

            var other = (Label)rows[0].GetChild(rows[0].ChildCount - 1);
            other.ForceRunStyleUpdate();
            Assert.That(other.TryGetStyleProperty<Color>(Label.StylePropertyFontColor, out var otherText), Is.True);
            Assert.That(otherText, Is.Not.EqualTo(green));
            window.Close();
        });
    }

    [Test]
    public async Task ListContainerUnderBaronIsUnchanged()
    {
        await SetTheme(false);
        var (window, list) = await ShowListContainer(4);

        await Client.WaitAssertion(() =>
        {
            Assert.That(list.TryGetStyleProperty<int>(ListContainer.StylePropertySeparation, out _), Is.False, "Baron keeps the 3px default");

            foreach (var row in Rows(list))
            {
                Assert.That(row.KsHasStripe, Is.False, "no stripe");
                Assert.That(row.TryGetStyleProperty<Color>(ListContainerButton.KsStylePropertyStripe, out _), Is.False);
                Assert.That(row.TryGetStyleProperty<Thickness>(ListContainerButton.KsStylePropertyPadding, out _), Is.False);
            }

            window.Close();
        });
    }

    [Test]
    public async Task KsTguiListSelectsActivatesAndMoves()
    {
        await SetTheme(true);

        KsTguiList list = null;
        var selected = new List<int>();
        var activated = new List<int>();
        await Client.WaitPost(() =>
        {
            list = new KsTguiList();
            list.OnSelected += selected.Add;
            list.OnActivated += activated.Add;
            for (var i = 0; i < 4; i++)
                list.AddRow($"Row {i}", metadata: i);

            UiManager.PopupRoot.AddChild(list);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(list.Count, Is.EqualTo(4));
            Assert.That(list.SelectedIndex, Is.Null);
            Assert.That(list.Rows.All(row => row.HasStyleClass(KsTguiColors.ButtonClass(KsTguiColor.Transparent))), Is.True, "transparent");
            Assert.That(list.Rows.All(row => row.Fluid), Is.True, "fluid");

            // A click selects, and only reports a change.
            list.PressRow(2, Vector2.Zero);
            Assert.That(selected, Is.EqualTo(new[] { 2 }));
            Assert.That(list.SelectedIndex, Is.EqualTo(2));
            Assert.That(list.Rows[2].Selected, Is.True);
            Assert.That(list.Rows[2].HasStyleClass(KsTguiSheetlet.StyleClassButtonSelectedTransparent), Is.True, "text green, background clear");
            Assert.That(list.Rows.Count(row => row.Selected), Is.EqualTo(1), "a single selection");
            Assert.That(activated, Is.Empty, "one click does not activate");

            // The second click on the same row, soon after, is tgui's onDoubleClick.
            list.PressRow(2, Vector2.Zero);
            Assert.That(activated, Is.EqualTo(new[] { 2 }));
            Assert.That(selected, Is.EqualTo(new[] { 2 }), "it was already selected");

            list.PressRow(0, Vector2.Zero);
            Assert.That(selected, Is.EqualTo(new[] { 2, 0 }));
            Assert.That(list.Rows[2].Selected, Is.False);

            // The arrow keys.
            list.MoveSelection(1);
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            list.MoveSelection(-1);
            list.MoveSelection(-1);
            Assert.That(list.SelectedIndex, Is.EqualTo(0), "stops at the first row");
            list.MoveSelection(10);
            Assert.That(list.SelectedIndex, Is.EqualTo(3), "and at the last");
            Assert.That(selected[^1], Is.EqualTo(3));

            // Setting it from code is silent.
            selected.Clear();
            list.Select(1);
            Assert.That(selected, Is.Empty);
            Assert.That(list.SelectedRow, Is.SameAs(list.Rows[1]));
            Assert.That(list.SelectedRow.Metadata, Is.EqualTo(1));

            // The rows are striped like any candystripe, first and third.
            Assert.That(list.IsStriped(list.Rows[0]), Is.True);
            Assert.That(list.IsStriped(list.Rows[1]), Is.False);
            Assert.That(list.IsStriped(list.Rows[2]), Is.True);

            list.Clear();
            Assert.That(list.Count, Is.EqualTo(0));
            Assert.That(list.SelectedIndex, Is.Null);
        });
    }

    /// <summary>
    ///     A drawing handle that keeps what it was told to draw, for the colours.
    /// </summary>
    private sealed class RecordingHandle : DrawingHandleScreen
    {
        public readonly List<Color> Primitives = new();
        public readonly List<Color> TextureColors = new();
        public Matrix3x2 Transform = Matrix3x2.Identity;

        public RecordingHandle() : base(null!)
        {
        }

        public bool DrewPrimitive(Color color) => Primitives.Contains(color);

        public override void SetTransform(in Matrix3x2 matrix) => Transform = matrix;

        public override Matrix3x2 GetTransform() => Transform;

        public override void UseShader(ShaderInstance shader)
        {
        }

        public override ShaderInstance GetShader() => null;

        public override void DrawPrimitives(DrawPrimitiveTopology primitiveTopology, Texture texture, ReadOnlySpan<DrawVertexUV2DColor> vertices)
        {
            foreach (var vertex in vertices)
            {
                Primitives.Add(vertex.Color);
            }
        }

        public override void DrawPrimitives(DrawPrimitiveTopology primitiveTopology, Texture texture, ReadOnlySpan<ushort> indices, ReadOnlySpan<DrawVertexUV2DColor> vertices)
        {
            DrawPrimitives(primitiveTopology, texture, vertices);
        }

        public override void DrawCircle(Vector2 position, float radius, Color color, bool filled = true)
        {
        }

        public override void DrawLine(Vector2 from, Vector2 to, Color color)
        {
        }

        public override void RenderInRenderTarget(IRenderTarget target, Action a, Color? clearColor)
        {
        }

        public override void DrawRect(UIBox2 rect, Color color, bool filled = true)
        {
            Primitives.Add(color);
        }

        public override void DrawTextureRectRegion(Texture texture, UIBox2 rect, UIBox2? subRegion = null, Color? modulate = null)
        {
            TextureColors.Add(modulate ?? Color.White);
        }

        public override void DrawEntity(Robust.Shared.GameObjects.EntityUid entity, Vector2 position, Vector2 scale, Angle? worldRot, Angle eyeRotation = default, Direction? overrideDirection = null, Robust.Client.GameObjects.SpriteComponent sprite = null, Robust.Shared.GameObjects.TransformComponent xform = null, Robust.Shared.GameObjects.SharedTransformSystem xformSystem = null)
        {
        }
    }
}
