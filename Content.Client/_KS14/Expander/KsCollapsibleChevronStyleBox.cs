using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.Expander;

/// <summary>
///     A <c>CollapsibleHeading</c>'s expander. The engine's chevron is a child texture that nothing restyles when the
///         heading is opened, since a pseudo-class change doesn't reach descendants. A style box on the heading itself
///         is picked by the heading's own pseudo-classes, so it can follow the state: a down chevron when open, a right
///         one when closed. Draws where the stock chevron sits (2px in, centred), which the stylesheet hides.
/// </summary>
public sealed class KsCollapsibleChevronStyleBox : StyleBox
{
    /// <summary>
    ///     Where the stock chevron sits, in virtual pixels.
    /// </summary>
    private const float LeftMargin = 2f;

    /// <summary>
    ///     Whether this is the open (down) chevron. Nothing reads it but a test: it is what the style rule chose.
    /// </summary>
    public bool Open;

    /// <summary>
    ///     The down chevron, as the engine's own would be drawn. Rotated for the closed state.
    /// </summary>
    public Texture? Texture;

    /// <summary>
    ///     Drawn in place of the texture, where the shape is made by a shader.
    /// </summary>
    public ShaderInstance? Shader;

    /// <summary>
    ///     The chevron's side in virtual pixels, for a shader. A texture uses its own size.
    /// </summary>
    public float ShaderSize = 16f;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var logical = Texture != null ? Texture.Size : new Vector2(ShaderSize);
        var size = logical * uiScale;
        var topLeft = new Vector2(box.Left + LeftMargin * uiScale, (box.Top + box.Bottom - size.Y) / 2f);
        var rect = UIBox2.FromDimensions(topLeft, size);

        // The handle's transform already places the control on screen, so the turn goes on top of it. Replacing it drew the
        //     closed chevron about the screen's corner, outside the heading's clip, so it never showed.
        var transform = handle.GetTransform();
        if (!Open)
            handle.SetTransform(Matrix3x2.CreateRotation(-MathF.PI / 2f, topLeft + size / 2f) * transform);

        if (Texture != null)
        {
            handle.DrawTextureRect(Texture, rect);
        }
        else
        {
            handle.UseShader(Shader);
            handle.DrawRect(rect, Color.White);
            handle.UseShader(null);
        }

        if (!Open)
            handle.SetTransform(transform);
    }
}
