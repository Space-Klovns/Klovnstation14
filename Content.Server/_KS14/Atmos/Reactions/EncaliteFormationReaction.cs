using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server._KS14.Atmos.Reactions;

/// <summary>
///     Creates encalite from frezon and plasma.
///     1 Frezon + 2 Plasma -> 3 Encalite at T <= 589 K.
/// </summary>
[UsedImplicitly]
public sealed partial class EncaliteFormationReaction : IGasReactionEffect
{
    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var initialFrezon = mixture.GetMoles(Gas.Frezon);
        var initialPlasma = mixture.GetMoles(Gas.Plasma);

        if (initialFrezon < 1 || initialPlasma < 2 || mixture.Temperature > 1800f)
            return ReactionResult.NoReaction;

        var consumedFrezon = Math.Min(initialFrezon, initialPlasma / 2f);
        var consumedPlasma = consumedFrezon * 2;
        var producedEncalite = consumedFrezon * 3;

        mixture.AdjustMoles(Gas.Frezon, -consumedFrezon);
        mixture.AdjustMoles(Gas.Plasma, -consumedPlasma);
        mixture.AdjustMoles(Gas.Encalite, producedEncalite);

        return ReactionResult.Reacting;
    }
}
