using Robust.Shared.Prototypes;

namespace Content.Server.Atmos.Reactions;

public sealed partial class GasReactionPrototype : IPrototype
{
    public IReadOnlyList<IGasReactionEffect> GetEffects()
        => _effects;
}
