using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Lobby.Kharkiv;

/// <summary>
///     Draws the main menu's light leaks (lobby_light_leaks.swsl) over its own box: anamorphic streaks, flare lines and
///     haze, added onto whatever is behind. Put it between the backdrop and the UI. It ignores the mouse.
/// </summary>
public sealed partial class KsKharkivLightLeaks : Control
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "KsLobbyLightLeaks";

    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private ShaderInstance? _shader;

    public KsKharkivLightLeaks()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
    }

    /// <summary>Overall strength of every leak.</summary>
    public float Intensity { get; set; } = 1f;

    /// <summary>Where the cold haze centres, as a fraction of the box from its top left: over the wordmark.</summary>
    public Vector2 HazeCentre { get; set; } = new(0.5f, 0.17f);

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (Intensity <= 0f || PixelSize.X <= 0 || PixelSize.Y <= 0)
            return;

        _shader ??= _prototypeManager.Index(ShaderId).InstanceUnique();

        // Every uniform is set: an instance with one unset draws with whatever value the program last had.
        _shader.SetParameter("quadPixelSize", (Vector2)PixelSize);
        _shader.SetParameter("uiScale", UIScale);
        _shader.SetParameter("intensity", Intensity);
        _shader.SetParameter("hazeCentre", HazeCentre);

        handle.UseShader(_shader);
        handle.DrawRect(PixelSizeBox, Color.White);
        handle.UseShader(null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shader?.Dispose();
            _shader = null;
        }

        base.Dispose(disposing);
    }
}
