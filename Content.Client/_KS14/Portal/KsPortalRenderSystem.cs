using System.Numerics;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client._KS14.Portal;

/// <summary>
///     One portal's captured view, ready to be composited into its window.
/// </summary>
/// <param name="Texture">The destination's surroundings, covering the world area of the viewer's main eye.</param>
/// <param name="Triangles">The visible window in world space (aperture plus shadow), as a triangle list.</param>
/// <param name="Quad">Where <paramref name="Texture"/>'s <paramref name="SubRegion"/> belongs in the world: the part of the main eye's view the window touches.</param>
/// <param name="SubRegion">The texture pixels, from the top left, that <paramref name="Quad"/> shows.</param>
/// <param name="MapId">The map the portal (and so its window) is on.</param>
public readonly record struct KsPortalDraw(Texture Texture, List<Vector2> Triangles, Box2Rotated Quad, UIBox2 SubRegion, MapId MapId);

/// <summary>
///     Settings for view portal rendering, and the hand-off of finished captures from the viewport that
///         renders them to the overlay that draws them.
/// </summary>
/// <remarks>
///     Feedback-loop safety lives here. <see cref="PassActive"/> is true for exactly the duration of the
///         pre-pass, and <see cref="ActiveViewport"/> is only set once every pre-pass render is finished and is
///         cleared in a finally after the viewer's own passes, so the overlay can only ever draw during the
///         viewer's own pass of the viewport that made the captures. A portal seen through a portal is
///         therefore drawn as its sprite only.
/// </remarks>
public sealed partial class KsPortalRenderSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;

    public bool Enabled { get; private set; } = true;
    public int MaxRendered { get; private set; } = 4;

    /// <summary>
    ///     Whether a portal pre-pass is rendering right now.
    /// </summary>
    /// <remarks>
    ///     Static so that overlays showing the viewer's own state or re-processing the screen can ask without
    ///         looking a system up: they have no business running in somebody else's view.
    /// </remarks>
    public static bool PassActive { get; internal set; }

    public float ResolutionScale { get; private set; } = 0.5f;
    public int FarUpdateInterval { get; private set; } = 1;
    public bool Lighting { get; private set; } = true;

    public IClydeViewport? ActiveViewport { get; private set; }

    public IReadOnlyList<KsPortalDraw> ActiveDraws { get; private set; } = [];

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.PortalRenderEnabled, value => Enabled = value, true);
        Subs.CVar(_configurationManager, KsCCVars.PortalMaxRendered, value => MaxRendered = value, true);
        Subs.CVar(_configurationManager, KsCCVars.PortalResolutionScale, value => ResolutionScale = value, true);
        Subs.CVar(_configurationManager, KsCCVars.PortalFarUpdateInterval, value => FarUpdateInterval = Math.Max(1, value), true);
        Subs.CVar(_configurationManager, KsCCVars.PortalLighting, value => Lighting = value, true);

        _overlayManager.AddOverlay(new KsPortalOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        ActiveViewport = null;
        ActiveDraws = [];
        PassActive = false;
        _overlayManager.RemoveOverlay<KsPortalOverlay>();
    }

    /// <summary>
    ///     Points the overlay at the captures belonging to the viewport that is about to draw.
    ///         Always paired with <see cref="ClearActive"/> in a finally.
    /// </summary>
    public void SetActive(IClydeViewport viewport, IReadOnlyList<KsPortalDraw> draws)
    {
        ActiveViewport = viewport;
        ActiveDraws = draws;
    }

    public void ClearActive()
    {
        ActiveViewport = null;
        ActiveDraws = [];
    }
}
