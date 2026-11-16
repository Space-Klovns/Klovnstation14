using Content.Shared._KS14.Cultivation.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     A secret manual. A cultivator who studies it learns its law, if they have room for it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CultivationManualComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CultivationLawPrototype> Law;

    /// <summary>
    ///     A master's transmission: even a mortal who studies it awakens as a cultivator.
    /// </summary>
    [DataField]
    public bool Awakens;
}

/// <summary>
///     Somewhere heaven and earth's qi pools: meditating near it gains more insight, and qi recovers faster.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CultivationQiSourceComponent : Component
{
    [DataField]
    public float Range = 5f;

    [DataField]
    public float Multiplier = 2f;
}
