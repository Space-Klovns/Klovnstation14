using Content.Shared.Materials;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Materials;

/// <summary>
///     What a predicted insert put in a machine, held on the client until the server says the same. Client-side only.
///     <para>
///         The inserted stack is deleted with prediction, so that it leaves the hand at once. That also leaves the replayed
///             click with nothing to insert, so when the next state rolls the storage back, nothing puts the amount back up,
///             and it would fall to what it was until the server's state arrives.
///     </para>
/// </summary>
[RegisterComponent, Access(typeof(MaterialStorageSystem))]
public sealed partial class KsHeldMaterialAmountsComponent : Component
{
    /// <summary>
    ///     The least each material shows as.
    /// </summary>
    [ViewVariables]
    public Dictionary<ProtoId<MaterialPrototype>, int> Amounts = new();

    /// <summary>
    ///     When to give up on the server, which may have refused the insert, and show what it says.
    /// </summary>
    [ViewVariables]
    public TimeSpan Until;

    /// <summary>
    ///     The insert's tick. Nothing the server says about an earlier one can confirm it.
    /// </summary>
    [ViewVariables]
    public GameTick InsertedTick;
}
