using Content.Client.Light;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._KS14.Light;

/// <summary>
///     Draws the AO of items resting on tables and other placeable surfaces onto those surfaces, using the textures
///     <see cref="AmbientOcclusionOverlay"/> builds earlier in the frame.
/// </summary>
public sealed partial class KsSurfaceAmbientOcclusionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> MaskedTextureShader = "KsMaskedTexture";

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly AmbientOcclusionOverlay _ambientOcclusionOverlay;
    private readonly ShaderInstance _maskedTextureShader;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public KsSurfaceAmbientOcclusionOverlay(AmbientOcclusionOverlay ambientOcclusionOverlay)
    {
        IoCManager.InjectDependencies(this);

        _ambientOcclusionOverlay = ambientOcclusionOverlay;
        _maskedTextureShader = _prototypeManager.Index(MaskedTextureShader).InstanceUnique();

        // Above surfaces (tables are at Objects), below the items on them.
        ZIndex = (int)DrawDepth.Items;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_ambientOcclusionOverlay.TryTakeSurfaceTextures(args.Viewport, out var aoTexture, out var maskTexture))
            return;

        var color = Color.FromHex(_configurationManager.GetCVar(CCVars.AmbientOcclusionColor));

        _maskedTextureShader.SetParameter("MASK", maskTexture);
        args.WorldHandle.UseShader(_maskedTextureShader);
        args.WorldHandle.DrawTextureRect(aoTexture, args.WorldBounds, color);
        args.WorldHandle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _maskedTextureShader.Dispose();
        base.DisposeBehavior();
    }
}
