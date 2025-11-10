using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Content.Shared._KS14.StainOverlays;

/// <summary>
///     Used for applying stains, visualised via overlays, onto things.
/// </summary>
public sealed class StainOverlaySystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;

    public EntityQuery<StainedOverlayComponent> StainedQuery;

    public override void Initialize()
    {
        base.Initialize();
        StainedQuery = GetEntityQuery<StainedOverlayComponent>();
    }

    /// <summary>
    ///     Ensures that <see cref="StainedOverlayComponent"/> exists on the entity,
    ///         adding it if it is not already present.
    /// </summary>
    /// <param name="component">Will not be null.</param>
    public void EnsureStainedComponent(EntityUid uid, [NotNull] ref StainedOverlayComponent? component)
    {
        if (StainedQuery.Resolve(uid, ref component, logMissing: false))
            return;

        component = AddComp<StainedOverlayComponent>(uid);
    }

    /// <summary>
    ///     Adds a stain to an entity with <see cref="StainedOverlayComponent"/>.
    /// </summary>
    private void AddOffsetStain(in Entity<StainedOverlayComponent> entity, in Vector2 offset, in Color color)
    {
        entity.Comp.Stains.Add((offset, color));
        Dirty(entity);

        EnsureComp<AppearanceComponent>(entity);
        _appearanceSystem.SetData(entity.Owner, StainOverlayVisuals.Count, entity.Comp.Stains.Count);
    }

    /// <summary>
    ///     Applies a stain to an entity, with a specified position offset from the center of
    ///         the entity.
    /// </summary>
    public void ApplyOffsetStain(Entity<StainedOverlayComponent?> entity, in Vector2 offset, in Color color)
    {
        EnsureStainedComponent(entity.Owner, ref entity.Comp);
        AddOffsetStain(entity!, offset, color);
    }

    /// <summary>
    ///     Applies a stain to an entity, coming from a given normalised direction towards the entity.
    /// </summary>
    // TODO: entities that arent 1-tile?
    public void ApplyDirectionalStain(Entity<StainedOverlayComponent?> entity, in Vector2 direction, in Color color)
    {
        EnsureStainedComponent(entity.Owner, ref entity.Comp);

        // Placeholder
        AddOffsetStain(entity!, -direction, color);
    }
}
