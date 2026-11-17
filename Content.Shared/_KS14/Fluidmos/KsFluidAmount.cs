using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     How much of one reagent a <see cref="KsFluidMixture"/> holds, in each phase.
/// </summary>
/// <remarks>
///     A linked reagent's vapor lives in its gas's moles in the <c>GasMixture</c>, so its <see cref="Vapor"/> stays 0.
/// </remarks>
[Serializable, NetSerializable]
public struct KsFluidAmount
{
    /// <summary>
    ///     Units of solid. Never moves with a transfer.
    /// </summary>
    public float Solid;

    /// <summary>
    ///     Units of liquid.
    /// </summary>
    public float Liquid;

    /// <summary>
    ///     Moles of vapor, for reagents with no linked gas.
    /// </summary>
    public float Vapor;

    public KsFluidAmount(float solid, float liquid, float vapor)
    {
        Solid = solid;
        Liquid = liquid;
        Vapor = vapor;
    }

    public readonly bool IsEmpty => Solid <= 0f && Liquid <= 0f && Vapor <= 0f;
}
