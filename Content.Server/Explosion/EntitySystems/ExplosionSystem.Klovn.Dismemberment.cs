// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

using System.Numerics;
using Content.Shared._KS14.BloodSpray;
using Content.Shared._KS14.Klovnmed;
using Content.Shared._KS14.Klovnmed.Dismemberment;
using Content.Shared.Body;
using Content.Shared.Body.Components;

namespace Content.Server.Explosion.EntitySystems;

// This partial is for KS14s klovnmeds dismemberment

public sealed partial class ExplosionSystem
{
    [Dependency] private readonly DismembermentSystem _dismembermentSystem = default!;
    [Dependency] private readonly BloodSpraySystem _bloodSpraySystem = default!;

    private readonly float[] _dismembermentTargetDistanceKeys = [0f, 0f, 1.25f, 2.5f, 7.5f]; // should be same length as below
    private readonly BodyPartType[] _dismembermentTargetDistanceValues = [BodyPartType.Foot, BodyPartType.Leg, BodyPartType.Hand, BodyPartType.Arm, BodyPartType.Head]; // should be same length as above

    // holy hardcoding
    private void HandleExplosionDamage(Entity<BodyComponent?> entity, float throwForce, float totalDamage, EntityUid? cause, Vector2 worldEpicenter, TransformComponent? transformComponent)
    {
        transformComponent ??= Transform(entity);

        var worldPosition = _transformSystem.GetWorldPosition(transformComponent);
        var positionalDelta = worldPosition - worldEpicenter;
        var distance = positionalDelta.Length();

        var eligiblePartTypeField = BodyPartType.Other;

        // point is that the further you get from epicenter the higher the potential damage area goes
        for (var i = 0; i < _dismembermentTargetDistanceValues.Length; i++)
        {
            if (distance < _dismembermentTargetDistanceKeys[i])
                break;

            eligiblePartTypeField |= _dismembermentTargetDistanceValues[i];
        }

        if (eligiblePartTypeField == BodyPartType.Other)
            return;

        Vector2Helpers.Normalize(ref positionalDelta);

        // maximum of 1 to (1 to 2) maximum number of dismemberments

        var distanceWithMinimumOne = Math.Max(1f, distance);

        // maximum potential dismemberments gets lower with distance
        var maximumDismemberments = Math.Min(Math.Max(1, (int)(3f / distanceWithMinimumOne)), _robustRandom.Next(1, 3)); // IDFK

        for (var i = 0; i < maximumDismemberments; i++)
        {
            _dismembermentSystem.TryDismemberRandomBodyPartOfType(
                (entity, entity.Comp, transformComponent),
                eligiblePartTypeField,
                out _,
                direction: positionalDelta,
                throwSpeed: throwForce * 0.65f,
                cause: cause
            );
        }

        if (TryComp<BloodstreamComponent>(entity.Owner, out var bloodstreamComponent))
        {
            _bloodSpraySystem.HandleBleedEffects(
                (entity.Owner, bloodstreamComponent),
                totalDamage,
                worldPosition,
                transformComponent,
                transformComponent.GridUid ?? transformComponent.ParentUid,
                positionalDelta,
                originUid: null
            );
        }
    }
}
