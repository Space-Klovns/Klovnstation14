using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Chemistry.Reaction;

namespace Content.Shared._KS14.OverlayStains;

/// <summary>
///     Used for applying stains, visualised via overlays, onto things.
/// </summary>
public sealed class StainSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;

    public EntityQuery<StainedComponent> StainedQuery;

    public override void Initialize()
    {
        base.Initialize();
        StainedQuery = GetEntityQuery<StainedComponent>();
    }

    /// <summary>
    ///     Removes <see cref="StainedComponent"/> from the specified uid.
    /// </summary>
    public void CleanEntity(EntityUid uid)
    {
        if (StainedQuery.TryGetComponent(uid, out var stainedComponent))
        {
            RemComp(uid, stainedComponent);
            RemComp<ReactiveComponent>(uid);
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
    }

    /// <summary>
    ///     Adds a stain to an entity with <see cref="StainedComponent"/>.
    /// </summary>
    private void AddOffsetStain(in Entity<StainedComponent> entity, in Vector2 offset, in Color color, float rotationScale = 0f)
    {
        entity.Comp.Stains.Add((new Vector3(offset.X, offset.Y, rotationScale), color));
        Dirty(entity);

        if (!EnsureComp<ReactiveComponent>(entity, out var reactiveComponent))
        {
            reactiveComponent.Reactions =
        }
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
