using Content.Shared._KS14.Damage.Pending;
using Content.Shared.Damage.Components;
using Robust.Client.GameObjects;

namespace Content.Client.Damage;

/// <summary>
///     Damage visuals follow pending damage (<see cref="KsPendingDamageSystem"/>): the wounds a predicted hit leaves show
///         with the hit, not a round trip later.
/// </summary>
/// <remarks>
///     The visuals are otherwise only gone over when the server's appearance data changes, which pending damage does not
///         touch. A change in what is pending queues the entity, and its layers are gone over from the damage as it is
///         asked for, pending included, on the next frame: not at once, as a kept copy's hit can land while a state
///         is being applied, when the damage asked for is the server's alone.
/// </remarks>
public sealed partial class DamageVisualsSystem
{
    /// <summary>
    ///     Entities whose pending damage changed since the last frame: gone over and emptied in <see cref="FrameUpdate"/>.
    /// </summary>
    private readonly HashSet<EntityUid> _ksPendingDamageChanged = new();

    [SubscribeLocalEvent]
    private void KsOnPendingDamageChanged(Entity<DamageVisualsComponent> entity, ref KsPendingDamageChangedEvent args)
    {
        _ksPendingDamageChanged.Add(entity.Owner);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_ksPendingDamageChanged.Count == 0)
            return;

        foreach (var uid in _ksPendingDamageChanged)
        {
            if (TerminatingOrDeleted(uid) ||
                !TryComp<DamageVisualsComponent>(uid, out var damageVisualsComponent) ||
                !damageVisualsComponent.Valid ||
                damageVisualsComponent.Disabled ||
                !TryComp<DamageableComponent>(uid, out var damageableComponent) ||
                !TryComp<SpriteComponent>(uid, out var spriteComponent))
                continue;

            ForceUpdateLayers((uid, damageableComponent, spriteComponent, damageVisualsComponent));
        }

        _ksPendingDamageChanged.Clear();
    }
}
