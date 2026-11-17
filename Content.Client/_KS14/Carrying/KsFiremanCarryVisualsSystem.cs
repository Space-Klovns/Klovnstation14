using Content.Shared._KS14.Carrying;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Carrying;

/// <summary>
///     Draws someone carried over their carrier's back rather than under them.
/// </summary>
public sealed partial class KsFiremanCarryVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;

    /// <summary>
    ///     Draw depth of each carried entity before it was lifted.
    /// </summary>
    private readonly Dictionary<EntityUid, int> _originalDrawDepths = new();

    [SubscribeLocalEvent]
    private void OnStartup(EntityUid uid, KsBeingCarriedComponent component, ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        _originalDrawDepths[uid] = sprite.DrawDepth;
        var carrierDepth = CompOrNull<SpriteComponent>(component.Carrier)?.DrawDepth ?? sprite.DrawDepth;
        _spriteSystem.SetDrawDepth((uid, sprite), carrierDepth + 1);
    }

    [SubscribeLocalEvent]
    private void OnCarryEnded(Entity<KsBeingCarriedComponent> carried, ref KsCarryEndedEvent args)
    {
        var uid = carried.Owner;
        if (!_originalDrawDepths.Remove(uid, out var drawDepth) || TerminatingOrDeleted(uid) ||
            !TryComp<SpriteComponent>(uid, out var sprite))
        {
            return;
        }

        _spriteSystem.SetDrawDepth((uid, sprite), drawDepth);
    }
}
