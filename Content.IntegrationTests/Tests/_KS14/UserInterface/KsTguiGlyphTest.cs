using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The checkbox, switch and chevron shapes are drawn by shaders chosen by style, so each has to follow its state.
/// </summary>
public sealed class KsTguiGlyphTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private IUserInterfaceManager UiManager => Client.ResolveDependency<IUserInterfaceManager>();

    private static ShaderInstance Shader(Control control)
    {
        control.TryGetStyleProperty<ShaderInstance>(TextureRect.StylePropertyShader, out var shader);
        return shader;
    }

    [Test]
    public async Task ToggleShapesFollowTheState()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var checkBox = new CheckBox { Text = "x" };
            var switchButton = new SwitchButton { Text = "x" };
            var option = new OptionButton();
            UiManager.PopupRoot.AddChild(checkBox);
            UiManager.PopupRoot.AddChild(switchButton);
            UiManager.PopupRoot.AddChild(option);

            void Restyle()
            {
                checkBox.ForceRunStyleUpdate();
                checkBox.TextureRect.ForceRunStyleUpdate();
                switchButton.ForceRunStyleUpdate();
                switchButton.TrackOutline.ForceRunStyleUpdate();
            }

            Restyle();
            var emptyBox = Shader(checkBox.TextureRect);
            var emptySwitch = Shader(switchButton.TrackOutline);
            Assert.That(emptyBox, Is.Not.Null);
            Assert.That(emptySwitch, Is.Not.Null);

            checkBox.Pressed = true;
            switchButton.Pressed = true;
            Restyle();
            var tickedBox = Shader(checkBox.TextureRect);
            Assert.That(tickedBox, Is.Not.Null.And.Not.SameAs(emptyBox), "Checked box shows the tick");
            Assert.That(Shader(switchButton.TrackOutline), Is.Not.Null.And.Not.SameAs(emptySwitch));

            checkBox.Pressed = false;
            switchButton.Pressed = false;
            Restyle();
            Assert.That(Shader(checkBox.TextureRect), Is.SameAs(emptyBox), "Unchecking goes back");
            Assert.That(Shader(switchButton.TrackOutline), Is.SameAs(emptySwitch));

            option.ForceRunStyleUpdate();
            foreach (var child in option.Children)
                child.ForceRunStyleUpdate();

            checkBox.Orphan();
            switchButton.Orphan();
            option.Orphan();
        });
    }

    /// <summary>
    ///     A heading's chevron is its own style box, chosen by the heading's pressed state, so opening it must change what
    ///         is drawn on both themes with no reopening.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task HeadingChevronFollowsOpenState(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var heading = new CollapsibleHeading("x");
            var collapsible = new Collapsible(heading, new CollapsibleBody());
            UiManager.PopupRoot.AddChild(collapsible);

            Content.Client._KS14.Expander.KsCollapsibleChevronStyleBox Box()
            {
                heading.ForceRunStyleUpdate();
                heading.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var box);
                return box as Content.Client._KS14.Expander.KsCollapsibleChevronStyleBox;
            }

            Assert.That(Box(), Is.Not.Null);
            Assert.That(Box()!.Open, Is.False, "Closed shows the right chevron");

            heading.Pressed = true;
            Assert.That(Box()!.Open, Is.True, "Opening swaps it");

            heading.Pressed = false;
            Assert.That(Box()!.Open, Is.False, "Closing swaps it back");

            collapsible.Orphan();
        });
    }

    /// <summary>
    ///     The stock chevron is a grandchild of the heading (heading, box, texture). A rule that never matched it left it drawn
    ///         beside the style box's own, so under the tgui theme it must be hidden; Baron's copy is not this test's to judge.
    /// </summary>
    [Test]
    public async Task TguiHidesTheStockHeadingChevron()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var heading = new CollapsibleHeading("x");
            var collapsible = new Collapsible(heading, new CollapsibleBody());
            UiManager.PopupRoot.AddChild(collapsible);

            var chevron = heading.Children.Single().Children.OfType<TextureRect>().Single();
            chevron.ForceRunStyleUpdate();
            Assert.That(chevron.TryGetStyleProperty<Color>(Control.StylePropertyModulateSelf, out var modulate), Is.True, "The tgui sheet should style the chevron.");
            Assert.That(modulate.A, Is.Zero, "The stock chevron should be transparent.");

            collapsible.Orphan();
        });
    }

    /// <summary>
    ///     The closed chevron is drawn turned, on top of the transform that places the heading on screen. Replacing that
    ///         transform drew it about the screen's corner, outside the heading's clip, so it was only ever seen open.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ClosedHeadingChevronIsDrawnWhereTheHeadingIs(bool open)
    {
        await Client.WaitAssertion(() =>
        {
            var placed = Matrix3x2.CreateTranslation(500f, 300f);
            var handle = new RecordingHandle(placed);
            var box = new Content.Client._KS14.Expander.KsCollapsibleChevronStyleBox { Open = open, ShaderSize = 16f };
            var bounds = new UIBox2(0f, 0f, 100f, 20f);

            box.Draw(handle, bounds, uiScale: 1f);

            Assert.That(handle.Drawn, Has.Count.EqualTo(1), "The chevron wasn't drawn once.");
            var (rect, transform) = handle.Drawn[0];
            var centre = Vector2.Transform(rect.Center, transform);
            var expected = Vector2.Transform(rect.Center, placed);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Vector2.Distance(centre, expected), Is.LessThan(0.01f), "The chevron was drawn away from its heading.");
                Assert.That(handle.GetTransform(), Is.EqualTo(placed), "The heading's transform wasn't put back after the chevron.");
            }
        });
    }

    /// <summary>
    ///     Records where each rectangle is drawn, and under what transform.
    /// </summary>
    private sealed class RecordingHandle(Matrix3x2 transform) : DrawingHandleScreen(Texture.White)
    {
        private Matrix3x2 _transform = transform;
        private ShaderInstance _shader;

        public readonly List<(UIBox2 Rect, Matrix3x2 Transform)> Drawn = new();

        public override void SetTransform(in Matrix3x2 matrix) => _transform = matrix;
        public override Matrix3x2 GetTransform() => _transform;
        public override void UseShader(ShaderInstance shader) => _shader = shader;
        public override ShaderInstance GetShader() => _shader;
        public override void DrawRect(UIBox2 rect, Color color, bool filled = true) => Drawn.Add((rect, _transform));
        public override void DrawTextureRectRegion(Texture texture, UIBox2 rect, UIBox2? subRegion = null, Color? modulate = null) =>
            Drawn.Add((rect, _transform));

        public override void DrawPrimitives(DrawPrimitiveTopology primitiveTopology, Texture texture, ReadOnlySpan<DrawVertexUV2DColor> vertices) { }
        public override void DrawPrimitives(DrawPrimitiveTopology primitiveTopology, Texture texture, ReadOnlySpan<ushort> indices, ReadOnlySpan<DrawVertexUV2DColor> vertices) { }
        public override void DrawCircle(Vector2 position, float radius, Color color, bool filled = true) { }
        public override void DrawLine(Vector2 from, Vector2 to, Color color) { }
        public override void RenderInRenderTarget(IRenderTarget target, Action a, Color? clearColor) { }
        public override void DrawTexture(Texture texture, Vector2 position, Color? modulate = null) { }

        public override void DrawEntity(EntityUid entity, Vector2 position, Vector2 scale, Angle? worldRot, Angle eyeRotation = default,
            Direction? overrideDirection = null, SpriteComponent sprite = null, TransformComponent xform = null,
            SharedTransformSystem xformSystem = null, IReadOnlyList<SpriteComponent.PostShaderEntry> postShaders = null) { }
    }
}
