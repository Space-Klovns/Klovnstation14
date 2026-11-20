using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Fluids.Evaporation;

/// <summary>
///     A game rule that, while active, makes each listed reagent evaporate from puddles at least at its rate, as if its
///         prototype said so. With several such rules, the highest rate for a reagent wins. Networked, and sent to every
///         client, since evaporation is predicted.
/// </summary>
/// <seealso cref="KsMinimumEvaporationSystem"/>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(KsMinimumEvaporationSystem))]
public sealed partial class KsMinimumEvaporationRuleComponent : Component
{
    /// <summary>
    ///     The least each reagent evaporates at, in units per second from a puddle holding only it, the same unit as a
    ///         reagent's <c>evaporationSpeed</c>. A reagent already faster keeps its own speed.
    /// </summary>
    // Read only in VV: a write there would bypass the rebuild that applies it.
    [DataField(required: true), AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> MinimumRates = new();

    /// <summary>
    ///     Whether the rule is running. Set by the server's game rule system; the client has no other way to know.
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public bool Active;
}
