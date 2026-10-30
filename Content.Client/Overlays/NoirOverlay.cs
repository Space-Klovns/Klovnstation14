using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

public sealed partial class NoirOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "Noir";

    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;
    private readonly ShaderInstance _noirShader;

    public NoirOverlay()
    {
        IoCManager.InjectDependencies(this);
        _noirShader = _prototypeManager.Index(Shader).InstanceUnique();
        ZIndex = 9; // draw this over the DamageOverlay, RainbowOverlay etc, but before the black and white shader
    }

    // KS14 start: not drawn into portal views (viewer state or screen-texture effect)
    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return !Content.Client._KS14.Portal.KsPortalRenderSystem.PassActive;
    }
    // KS14 end

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _noirShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        handle.UseShader(_noirShader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
