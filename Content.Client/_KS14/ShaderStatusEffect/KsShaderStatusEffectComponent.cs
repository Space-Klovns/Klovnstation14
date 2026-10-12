using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.ShaderStatusEffect;

/// <summary>
///     Put on a status effect entity. While the effect is active on the local player, <see cref="Shader"/> is
///         applied over the whole screen by <see cref="KsShaderStatusEffectOverlay"/>.
/// </summary>
/// <remarks>
///     The shader must sample <c>SCREEN_TEXTURE</c>. Several active effects chain, each one's
///         <c>SCREEN_TEXTURE</c> being the output of the one before it.
/// </remarks>
[RegisterComponent]
public sealed partial class KsShaderStatusEffectComponent : Component
{
    /// <summary>
    ///     Shader applied to the screen. Every effect entity gets its own unique instance of it.
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
}
