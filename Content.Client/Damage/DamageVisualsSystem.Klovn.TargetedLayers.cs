// KS14: added in this fork
using Content.Client._KS14.Damage; // KS14
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint; // KS14
using Robust.Client.GameObjects;

namespace Content.Client.Damage;

public sealed partial class DamageVisualsSystem
{
    public void TryForceUpdateLayers(Entity<DamageableComponent?, SpriteComponent?, DamageVisualsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp1, logMissing: false) ||
            !Resolve(entity, ref entity.Comp2, logMissing: false) ||
            !Resolve(entity, ref entity.Comp3, logMissing: false))
            return;

        // Layer visibility has to be refreshed separately - ForceUpdateLayers only walks damage groups,
        // and it bails out early on every layer whose threshold did not move.
        KsUpdateDisabledLayers((entity.Owner, entity.Comp2, entity.Comp3, null));

        ForceUpdateLayers(entity!);
    }

    /// <summary>
    ///     Refreshes which damage overlay layers are hidden, outside of an appearance change.
    ///     Silently does nothing on entities that do not use targeted damage overlay layers.
    /// </summary>
    public void KsUpdateDisabledLayers(Entity<SpriteComponent?, DamageVisualsComponent?, AppearanceComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp1, logMissing: false) ||
            !Resolve(entity, ref entity.Comp2, logMissing: false) ||
            !Resolve(entity, ref entity.Comp3, logMissing: false))
            return;

        var damageVisualsComponent = entity.Comp2;

        if (!damageVisualsComponent.Valid || damageVisualsComponent.Disabled)
            return;

        // Mirrors the guard in HandleDamage - DisabledLayers is only populated for these two setups.
        if (damageVisualsComponent.TargetLayers == null ||
            (damageVisualsComponent.DamageOverlayGroups == null && damageVisualsComponent.DamageOverlay == null))
            return;

        UpdateDisabledLayers(entity.Owner, entity.Comp1, entity.Comp3, damageVisualsComponent);
    }

    /// <summary>
    ///     Raises <see cref="KsGetDamageVisualsEvent"/> to collect the layer map keys that should be hidden
    ///     no matter how damaged the entity is - because the limb owning them is missing, usually.
    /// </summary>
    /// <returns>The collected layer map keys, or null if nothing wants any of them hidden.</returns>
    private List<Enum>? KsGetForcefullyHiddenLayerMapKeys(EntityUid uid, DamageVisualsComponent damageVisualsComponent)
    {
        var getDamageVisualsEvent = new KsGetDamageVisualsEvent(damageVisualsComponent, null);
        RaiseLocalEvent(uid, ref getDamageVisualsEvent);

        return getDamageVisualsEvent.RemovedLayerMapKeys;
    }

    /// <summary>
    ///     Shows or hides one reserved damage overlay layer.
    /// </summary>
    /// <remarks>
    ///     Damage updates skip disabled layers, so a layer that is being shown again may be sitting on a
    ///     stale RSI state - or on no damage at all. Both are resolved by re-running the state update
    ///     against the threshold the layer should currently be at.
    /// </remarks>
    private void KsSetDamageLayerDisabled(
        Entity<SpriteComponent> spriteEntity,
        DamageVisualsComponent damageVisualsComponent,
        object layerMapKey,
        string spriteLayerMapKey,
        string? damageGroup,
        FixedPoint2 lastThreshold,
        bool disabled)
    {
        if (!SpriteSystem.LayerMapTryGet(spriteEntity.AsNullable(), spriteLayerMapKey, out var spriteLayer, false))
            return;

        if (disabled)
        {
            SpriteSystem.LayerSetVisible(spriteEntity.AsNullable(), spriteLayer, false);
            return;
        }

        if (!damageVisualsComponent.LayerMapKeyStates.TryGetValue(layerMapKey, out var layerState))
            return;

        UpdateDamageLayerState(
            spriteEntity,
            spriteLayer,
            damageGroup == null ? layerState : $"{layerState}_{damageGroup}",
            lastThreshold);
    }
}
