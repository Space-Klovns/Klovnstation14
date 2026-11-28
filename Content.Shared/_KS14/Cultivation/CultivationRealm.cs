namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     The great realms of cultivation, in order. Each breakthrough moves a cultivator one step up.
/// </summary>
public enum CultivationRealm : byte
{
    QiCondensation = 0,
    FoundationEstablishment = 1,
    GoldenCore = 2,
    NascentSoul = 3,
}

/// <summary>
///     The five phases a cultivation law can be built on.
/// </summary>
public enum CultivationElement : byte
{
    Metal,
    Wood,
    Water,
    Fire,
    Earth,
}
