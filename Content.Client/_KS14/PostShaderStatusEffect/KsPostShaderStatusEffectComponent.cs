using Content.Client._KS14.ShaderStatusEffect;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.PostShaderStatusEffect;

/// <summary>
///     Put on a status effect entity. While the effect is active, <see cref="Shader"/> is applied as a post-shader to
///         the sprite of whatever the effect is on, for everyone who can see it.
/// </summary>
/// <remarks>
///     The sprite counterpart of <see cref="KsShaderStatusEffectComponent"/>, which shades the whole screen of the
///         player the effect is on instead. A post-shader draws the finished, already lit sprite, so the shader should
///         be <c>light_mode unshaded</c> and sample <c>TEXTURE</c>, not <c>SCREEN_TEXTURE</c>.
/// </remarks>
[RegisterComponent]
[Access(typeof(KsPostShaderStatusEffectSystem))]
public sealed partial class KsPostShaderStatusEffectComponent : Component
{
    /// <summary>
    ///     Shader applied to the sprite. Every effect entity gets its own unique instance of it.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ShaderPrototype> Shader;

    /// <summary>
    ///     Uniform values set once, when this effect's shader instance is created.
    /// </summary>
    [DataField]
    public Dictionary<string, KsShaderParameter> Parameters = new();

    /// <summary>
    ///     If set, the name of a float uniform that is given, every frame, the ratio of this effect's duration still
    ///         left - 1 when it starts, 0 when it ends. An effect with no end time gives 1.
    /// </summary>
    [DataField]
    public string? TimeLeftParameter;

    /// <summary>
    ///     If set, the name of a float uniform that is given a number unique to this effect entity, for shaders that
    ///         would otherwise animate two affected sprites in lockstep.
    /// </summary>
    [DataField]
    public string? SeedParameter;

    /// <summary>
    ///     This effect's shader instance, created the first time it is applied.
    /// </summary>
    [ViewVariables]
    public ShaderInstance? ShaderInstance;

    /// <summary>
    ///     The entity whose sprite currently carries this effect's post-shader, if any.
    /// </summary>
    [ViewVariables]
    public EntityUid? ShadedUid;
}
