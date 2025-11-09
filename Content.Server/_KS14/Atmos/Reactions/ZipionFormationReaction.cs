// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server._KS14.Atmos.Reactions;

/// <summary>
///     Creates zipion, the atmos baby learning encouragement gas.
/// </summary>
[UsedImplicitly]
public sealed partial class ZipionFormationReaction : IGasReactionEffect
{
    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var initialVapor = mixture.GetMoles(Gas.WaterVapor);
        var initialPlasma = mixture.GetMoles(Gas.Plasma);

        if (initialPlasma < 1 || initialVapor < 9 || mixture.Temperature > KsAtmospherics.ZipionProductionThresholdTemperature) // super low reaction threshold = you can use expansion chambers for reacting (very useful for internalising pv=nrt)
            return ReactionResult.NoReaction;

        // are we being limited by the production rate or by the vapor available?
        var consumedPlasma = Math.Min(initialPlasma / KsAtmospherics.ZipionProductionConversionRate, (initialVapor - (initialVapor % 9)) / 9); //i dont want to fuck with this fucking programming languages integer division + compilers gonna make it all the same anyway
        var consumedVapor = consumedPlasma * 9;
        var producedZipion = consumedPlasma + consumedVapor;

        // this is deliberately made to be simple and stupid to make. perhaps in the future I will add heat generation to the reaction if it proves too simple, but I want this to be a rewarding and encouraging atmos gas for atmos fetuses
        mixture.AdjustMoles(Gas.Plasma, -consumedPlasma);
        mixture.AdjustMoles(Gas.WaterVapor, -consumedVapor);
        mixture.AdjustMoles(Gas.Zipion, producedZipion);

        return ReactionResult.Reacting;
    }
}
