using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reaction;

namespace Content.Shared._KS14.OverlayStains;

/// <summary>
///     Used for applying stains, visualised via overlays, onto things.
/// </summary>
public sealed class StainSystem : EntitySystem
{
    public EntityQuery<StainedComponent> StainedQuery;

    /// <summary>
    ///     Wrapper for a reaction triggered by water, space-cleaner
    ///         and bleach, that effects <see cref="StainCleanReaction"/>. 
    /// </summary>
    public ReactiveReagentEffectEntry StainCleanEffectEntry = default!;

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
            RemComp(uid, stainedComponent);

            if (TryComp<ReactiveComponent>(uid, out var reactiveComponent))
            {
                reactiveComponent.Reactions?.Remove(StainCleanEffectEntry);

                // clean up
                if (stainedComponent.OwnsBoundReactiveComponent)
                {
                    stainedComponent.OwnsBoundReactiveComponent = false;
                    RemComp(uid, reactiveComponent);
                }
            }
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
    ///     Adds a stain to an entity with <see cref="StainedComponent"/> and
    ///         does necessary logic to handle doing so.
    /// </summary>
    private void AddOffsetStain(in Entity<StainedComponent> entity, in Vector2 offset, in Color color, float rotationScale = 0f)
    {
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
}
