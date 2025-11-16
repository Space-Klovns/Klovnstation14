// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.Deferral;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reaction;
using Robust.Shared.Map;

namespace Content.Shared._KS14.OverlayStains;

/// <summary>
///     Used for applying stains, visualised via overlays, onto things.
/// </summary>
public sealed class StainSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;

    public EntityQuery<StainedComponent> StainedQuery;

    /// <summary>
    ///     Wrapper for a reaction triggered by water, space-cleaner
    ///         and bleach, that effects <see cref="StainCleanReaction"/>. 
    /// </summary>
    public ReactiveReagentEffectEntry StainCleanEffectEntry = default!;

    /// <summary>
    ///     Maximum number of stains on one entity. When trying to create
    ///         a stain on an entity that is at the maximum number of stains,
    ///         the oldest stain will be truncated first.
    /// </summary>
    public const int MaxStains = 25;

    public override void Initialize()
    {
        base.Initialize();

        StainedQuery = GetEntityQuery<StainedComponent>();
        StainCleanEffectEntry = new()
        {
            Methods = new() { ReactionMethod.Touch },
            Reagents = new() { "Water", "SpaceCleaner", "Bleach" }, // TODO: Un-hardcode
            Effects = new() { new StainCleanReaction() }
        };
    }

    /// <summary>
    ///     Removes <see cref="StainedComponent"/> from the specified uid.
    /// </summary>
    public void CleanEntity(EntityUid uid)
    {
        if (StainedQuery.TryGetComponent(uid, out var stainedComponent))
        {
            if (TryComp<ReactiveComponent>(uid, out var reactiveComponent))
            {
                SynchronousDeferralSystem.Defer(() => reactiveComponent.Reactions?.Remove(StainCleanEffectEntry));

                // clean up
                if (stainedComponent.OwnsBoundReactiveComponent)
                {
                    stainedComponent.OwnsBoundReactiveComponent = false;
                    RemCompDeferred(uid, reactiveComponent);
                }
            }

            RemComp(uid, stainedComponent);
        }
    }

    /// <summary>
    ///     Ensures that <see cref="StainedComponent"/> exists on the entity,
    ///         adding it if it is not already present.
    /// </summary>
    /// <param name="component">Will not be null.</param>
    public void EnsureStainedComponent(EntityUid uid, [NotNull] ref StainedComponent? component)
    {
        if (StainedQuery.Resolve(uid, ref component, logMissing: false))
            return;

        component = AddComp<StainedComponent>(uid);
        Dirty(uid, component);
    }

    /// <summary>
    ///     Adds a stain to an entity with an existing <see cref="StainedComponent"/> and
    ///         does necessary logic to handle doing so.
    /// </summary>
    public void AddOffsetStain(in Entity<StainedComponent> entity, in Vector2 offset, in Color color, float rotationScale = 0f)
    {
        if (entity.Comp.Stains.Count >= MaxStains)
            entity.Comp.Stains.RemoveAt(0); // remove oldest

        entity.Comp.Stains.Add((new Vector3(offset.X, offset.Y, rotationScale), color));

        var ownsBoundReactiveComponent = !EnsureComp<ReactiveComponent>(entity, out var reactiveComponent);
        if (ownsBoundReactiveComponent || (!reactiveComponent.Reactions?.Contains(StainCleanEffectEntry) ?? false))
        {
            // only set to true if we made a reactivecomponent on the entity, otherwise don't
            entity.Comp.OwnsBoundReactiveComponent |= ownsBoundReactiveComponent;

            reactiveComponent.Reactions ??= new();
            reactiveComponent.Reactions.Add(StainCleanEffectEntry);
        }

        Dirty(entity);
        Log.Debug($"Applying stain at offset: {offset}, for entity: {ToPrettyString(entity.Owner)}");
    }

    /// <summary>
    ///     Applies a stain to an entity, with a specified position offset from the center of
    ///         the entity.
    /// </summary>
    public void ApplyOffsetStain(Entity<StainedComponent?> entity, in Vector2 offset, in Color color, float rotationScale = 0f)
    {
        EnsureStainedComponent(entity.Owner, ref entity.Comp);
        AddOffsetStain(entity!, offset, color, rotationScale);
    }

    /// <summary>
    ///     Applies a stain to an entity, coming from some <see cref="EntityCoordinates"/>.
    /// </summary>
    /// <param name="offset">Offset to apply to source position.</param>
    public void ApplyStain(Entity<TransformComponent?, StainedComponent?> entity, in EntityCoordinates sourceCoordinates, in Color color, float rotationScale = 0f, float coefficient = 1f)
    {
        EntityManager.TransformQuery.Resolve(entity, ref entity.Comp1);
        EnsureStainedComponent(entity.Owner, ref entity.Comp2);

        Vector2 sourceRelativeToEntityPosition;

        // just do relative if we can save a bit of calculation with it
        if (entity.Comp1!.ParentUid == sourceCoordinates.EntityId)
            sourceRelativeToEntityPosition = entity.Comp1.LocalPosition - sourceCoordinates.Position;
        else
        {
            var sourceWorldPosition = Vector2.Transform(sourceCoordinates.Position, _transformSystem.GetWorldMatrix(sourceCoordinates.EntityId));
            sourceRelativeToEntityPosition = entity.Comp1.LocalPosition - Vector2.Transform(sourceWorldPosition, _transformSystem.GetInvWorldMatrix(entity.Comp1!.ParentUid));
        }

        AddOffsetStain((entity, entity.Comp2), sourceRelativeToEntityPosition.Normalized() * -coefficient, color, rotationScale);
        Log.Debug($"At source: (local) {sourceCoordinates.Position}");
    }
}
