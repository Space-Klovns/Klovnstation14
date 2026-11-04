using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Chemistry.InfiniteDispenser;

/// <summary>
///     A reagent dispenser that makes these reagents from nothing, as /tg/'s chem dispenser does, instead of pouring them
///         from jugs. Jugs put in its storage still pour as before, next to these.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsInfiniteReagentDispenserComponent : Component
{
    /// <summary>
    ///     What it makes. A request for anything else is refused: the request comes from the client.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public List<ProtoId<ReagentPrototype>> Reagents = new();
}
