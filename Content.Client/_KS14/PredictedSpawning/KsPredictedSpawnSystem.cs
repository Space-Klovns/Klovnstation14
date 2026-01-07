// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.PredictedSpawning;
using Robust.Client.Physics;

namespace Content.Client._KS14.PredictedSpawning;

/// <inheritdoc/>
public sealed class KsPredictedSpawnSystem : KsSharedPredictedSpawnSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsPredictedSpawnComponent, UpdateIsPredictedEvent>(OnPredictedSpawnCheckPhysicsPrediction);
    }

    private void OnPredictedSpawnCheckPhysicsPrediction(Entity<KsPredictedSpawnComponent> entity, ref UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }
}
