using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Mortar;

/// <summary>
///     Drawn over the mortar's sight: a cross at the aim, at the centre of the view, and the ring its shells may land in,
///         to the scale of a view <see cref="SightRadius"/> tiles from the centre to its edge.
/// </summary>
public sealed class KsMortarSightReticle : Control
{
    private static readonly Color ReticleColor = Color.FromHex("#6CFF6C");

    /// <summary>
    ///     How far from the aim a shell may land, in tiles.
    /// </summary>
    public float Spread { get; set; }

    /// <summary>
    ///     Tiles from the centre of the view to its edge.
    /// </summary>
    public float SightRadius { get; set; } = 1f;

    public KsMortarSightReticle()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (SightRadius <= 0f || !float.IsFinite(SightRadius) || !float.IsFinite(Spread))
            return;

        var size = (Vector2) PixelSize;
        var centre = size / 2f;
        var tile = MathF.Min(size.X, size.Y) / 2f / SightRadius;
        var arm = 4f * UIScale;

        handle.DrawLine(centre - new Vector2(arm, 0f), centre + new Vector2(arm, 0f), ReticleColor);
        handle.DrawLine(centre - new Vector2(0f, arm), centre + new Vector2(0f, arm), ReticleColor);
        handle.DrawCircle(centre, MathF.Max(Spread * tile, 2f * UIScale), ReticleColor.WithAlpha(0.8f), filled: false);
    }
}
