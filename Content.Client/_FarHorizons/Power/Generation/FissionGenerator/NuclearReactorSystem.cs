// SPDX-FileCopyrightText: 2025 jhrushbe
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Robust.Client.GameObjects;

namespace Content.Client._FarHorizons.Power.Generation.FissionGenerator;

public sealed class NuclearReactorSystem : SharedNuclearReactorSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NuclearReactorComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(Entity<NuclearReactorComponent> entity, ref AppearanceChangeEvent args)
    {
        if (!AppearanceSystem.TryGetData<bool>(entity.Owner, ReactorVisuals.Smoke, out var isNowSmoking, args.Component) ||
            !AppearanceSystem.TryGetData<bool>(entity.Owner, ReactorVisuals.Fire, out var isNowBurning, args.Component))
            return;

        UpdateTempIndicators(entity, isNowSmoking, isNowBurning);
    }
}
