using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.OreWell;

[RegisterComponent]
public sealed partial class OreWellReceiverComponent : Component
{
    [DataField]
    public string? FlickLayerKey = null;

    [DataField]
    public string FlickState = "";

    /// <summary>
    ///     Sound played when receiving ore.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? Sound = null;

    /// <summary>
    ///     How much is owed to be spawned in the next tick.
    ///
    ///     This exists so that rate of ore generated isn't fucked up.
    ///         Elements dont get purged from this when their value is 0.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<ProtoId<StackPrototype>, float> Debt = [];
}
