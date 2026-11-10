using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.ShaderStatusEffect;

/// <summary>
///     A single typed uniform value to set on a shader, keyed by uniform name in
///         <see cref="KsShaderStatusEffectComponent.Parameters"/>.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class KsShaderParameter
{
    public abstract void Apply(ShaderInstance shaderInstance, string name);
}

public sealed partial class KsShaderFloat : KsShaderParameter
{
    [DataField(required: true)]
    public float Value;

    public override void Apply(ShaderInstance shaderInstance, string name)
    {
        shaderInstance.SetParameter(name, Value);
    }
}

public sealed partial class KsShaderInt : KsShaderParameter
{
    [DataField(required: true)]
    public int Value;

    public override void Apply(ShaderInstance shaderInstance, string name)
    {
        shaderInstance.SetParameter(name, Value);
    }
}

public sealed partial class KsShaderBool : KsShaderParameter
{
    [DataField(required: true)]
    public bool Value;

    public override void Apply(ShaderInstance shaderInstance, string name)
    {
        shaderInstance.SetParameter(name, Value);
    }
}

public sealed partial class KsShaderVector2 : KsShaderParameter
{
    [DataField(required: true)]
    public Vector2 Value;

    public override void Apply(ShaderInstance shaderInstance, string name)
    {
        shaderInstance.SetParameter(name, Value);
    }
}

public sealed partial class KsShaderColor : KsShaderParameter
{
    [DataField(required: true)]
    public Color Value;

    public override void Apply(ShaderInstance shaderInstance, string name)
    {
        shaderInstance.SetParameter(name, Value);
    }
}
