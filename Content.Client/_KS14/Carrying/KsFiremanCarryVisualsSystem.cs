using Content.Shared._KS14.Carrying;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Carrying;

/// <summary>
///     Draws someone carried over their carrier's back rather than under them.
/// </summary>
public sealed partial class KsFiremanCarryVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;

    // ComponentStartup is a by-value class event, which has no Entity<T> handler form.
    [SubscribeLocalEvent]
    private void OnStartup(EntityUid uid, KsBeingCarriedComponent component, ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        // Already raised, by a carry that state is re-applying: the depth to restore is the one remembered then.
        if (!EnsureComp<KsCarriedVisualsComponent>(uid, out var visuals))
            visuals.OriginalDrawDepth = sprite.DrawDepth;

        var carrierDepth = CompOrNull<SpriteComponent>(component.Carrier)?.DrawDepth ?? visuals.OriginalDrawDepth;
        _spriteSystem.SetDrawDepth((uid, sprite), carrierDepth + 1);
    }

    [SubscribeLocalEvent]
    private void OnCarryEnded(Entity<KsBeingCarriedComponent> carried, ref KsCarryEndedEvent args)
    {
        var uid = carried.Owner;
        if (TerminatingOrDeleted(uid) || !TryComp<KsCarriedVisualsComponent>(uid, out var visuals))
            return;

        if (TryComp<SpriteComponent>(uid, out var sprite))
            _spriteSystem.SetDrawDepth((uid, sprite), visuals.OriginalDrawDepth);

        // Nothing enumerates these, and the next carry must find it gone to remember the depth afresh.
        RemComp(uid, visuals);
    }
}
