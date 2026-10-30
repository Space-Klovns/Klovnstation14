using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    /// Whether emissive layers bloom: a blurred copy of their glow added over the lit world.
    /// </summary>
    public static readonly CVarDef<bool> EmissiveBloom =
        CVarDef.Create("klovn.emissive.bloom", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// How bright the emissive bloom is added.
    /// </summary>
    public static readonly CVarDef<float> EmissiveBloomStrength =
        CVarDef.Create("klovn.emissive.bloom_strength", 0.375f, CVar.CLIENTONLY);

    /// <summary>
    /// How far the emissive bloom spreads, as a blur multiplier.
    /// </summary>
    public static readonly CVarDef<float> EmissiveBloomRadius =
        CVarDef.Create("klovn.emissive.bloom_radius", 7.5f, CVar.CLIENTONLY);
}
