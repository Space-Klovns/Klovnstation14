// SPDX-FileCopyrightText: 2025 Deltium
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 Syndicate Engineer
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
// SPDX-FileCopyrightText: 2025 syndicate-engineer
//
// SPDX-License-Identifier: MIT

using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server._KS14.Atmos.Reactions;

/// <summary>
///     When argon reaches 80 moles, it slowly absorbs other gases and stops all reactions.
/// </summary>
[UsedImplicitly]
public sealed partial class ArgonAbsorptionReaction : IGasReactionEffect
{
    private const float AbsorptionRate = 2f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var argonMoles = mixture.GetMoles(Gas.Argon);

        if (argonMoles < 80f)
            return ReactionResult.NoReaction;

        float totalAbsorbed = 0f;

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            if (i == (int)Gas.Argon)
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
            return ReactionResult.Reacting | ReactionResult.StopReactions;
        }

        return ReactionResult.StopReactions;
    }
}
