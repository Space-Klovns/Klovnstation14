using Robust.Client.GameObjects;
using Content.Shared._KS14.NPC.Events;

namespace Content.Client._KS14.NPC.Systems;

/// <summary>
/// Client-side system for handling NPC attack telegraph visuals
/// </summary>
public sealed partial class NPCRangedAttackVisualizerSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<NPCRangedTelegraphNetworkEvent>(OnTelegraphEvent);
    }

    private void OnTelegraphEvent(NPCRangedTelegraphNetworkEvent ev)
    {
        var owner = GetEntity(ev.Owner);

        if (TryComp<SpriteComponent>(owner, out var sprite))
        {
            _spriteSystem.LayerSetRsiState((owner, sprite), 0, ev.SpriteState);
        }
    }
}
