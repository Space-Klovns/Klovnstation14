using Content.Shared._KS14.Particles;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Impacts;

/// <summary>
///     What a bullet's impact on a surface looks and sounds like: TGMC's debris element (<c>/datum/element/debris</c>),
///         one of its <c>DEBRIS_*</c> kinds. A surface names one with <see cref="KsImpactSurfaceComponent"/>.
/// </summary>
[Prototype]
public sealed partial class KsImpactMaterialPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Thrown back out of the surface by a ballistic hit. Energy shots throw none, as in TGMC.
    /// </summary>
    [DataField]
    public ProtoId<KsParticlesPrototype>? Debris;

    /// <summary>
    ///     Puffed out of the surface by every hit.
    /// </summary>
    [DataField]
    public ProtoId<KsParticlesPrototype>? Smoke = "KsImpactSmoke";

    /// <summary>
    ///     Played by a ballistic hit, with <see cref="SoundChance"/>: TGMC's <c>sound_bounce</c>.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public float SoundChance = 0.5f;
}
