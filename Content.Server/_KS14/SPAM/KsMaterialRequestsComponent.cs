using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.SPAM;

/// <summary>
///     On a silo: the materials the lathes have asked the salvagers to bring it.
/// </summary>
[RegisterComponent]
[Access(typeof(KsMaterialRequestSystem))]
public sealed partial class KsMaterialRequestsComponent : Component
{
    /// <summary>
    ///     How much of each material the silo has to hold for the request to be met.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> Targets = [];
}
