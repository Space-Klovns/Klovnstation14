using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server._KS14.Atmos.Reactions;

/// <summary>
///     When encalite reaches 80 moles, it slowly absorbs other gases and stops all reactions.
/// </summary>
[UsedImplicitly]
public sealed partial class EncaliteAbsorptionReaction : IGasReactionEffect
{
    private const float AbsorptionRate = 2f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var encaliteMoles = mixture.GetMoles(Gas.Encalite);

        if (encaliteMoles < 80f)
            return ReactionResult.NoReaction;

        float totalAbsorbed = 0f;

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            if (i == (int)Gas.Encalite)
                continue;

            var moles = mixture.GetMoles(i);
            if (moles <= 0f)
                continue;

            var absorb = Math.Min(AbsorptionRate, moles);
            mixture.AdjustMoles(i, -absorb);
            totalAbsorbed += absorb;
        }

        if (totalAbsorbed > 0f)
        {
            // Infinite encalite
            mixture.AdjustMoles(Gas.Encalite, totalAbsorbed);

            return ReactionResult.Reacting | ReactionResult.StopReactions;
        }

        return ReactionResult.StopReactions;
    }
}
