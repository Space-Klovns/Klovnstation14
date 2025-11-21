// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 jhrushbe
// SPDX-FileCopyrightText: 2025 rottenheadphones
//
// SPDX-License-Identifier: MPL-2.0

using Content.Client.Examine;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._FarHorizons.Power.Generation.FissionGenerator;

public sealed class NuclearReactorSystem : SharedNuclearReactorSystem
{
    private static readonly EntProtoId ArrowPrototype = "ReactorFlowArrow";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NuclearReactorComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<NuclearReactorComponent, ClientExaminedEvent>(OnClientExamined);
    }

    private void OnClientExamined(Entity<NuclearReactorComponent> entity, ref ClientExaminedEvent _) => Spawn(ArrowPrototype, new EntityCoordinates(entity.Owner, 0f, 0f));

    private void OnAppearanceChange(Entity<NuclearReactorComponent> entity, ref AppearanceChangeEvent args)
    {
        if (AppearanceSystem.TryGetData<bool>(entity.Owner, ReactorVisuals.Smoke, out var isSmoking, args.Component))
        {
            if (!entity.Comp.isSmoking && isSmoking)
                UpdateTempIndicators(entity, true, null);
            else if (entity.Comp.isSmoking && !isSmoking)
                UpdateTempIndicators(entity, false, null);
        }

        if (AppearanceSystem.TryGetData<bool>(entity.Owner, ReactorVisuals.Fire, out var isBurning, args.Component))
        {
            if (!entity.Comp.isBurning && isBurning)
                UpdateTempIndicators(entity, null, true);
            else if (entity.Comp.isBurning && !isBurning)
                UpdateTempIndicators(entity, null, false);
        }
    }
}
