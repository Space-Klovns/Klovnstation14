using Content.Shared._KS14.Anchorless.Components;

namespace Content.Server._KS14.Anchorless.Systems;

/// <summary>
/// Copies the authoritative horror form into a public component so its sprite
/// state is replicated to every client in PVS without exposing stored identities.
/// </summary>
public sealed partial class AnchorlessHorrorVisualSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<KsAnchorlessAntagComponent, AnchorlessHorrorFormChangedEvent>(OnHorrorFormChanged);
    }

    private void OnHorrorFormChanged(Entity<KsAnchorlessAntagComponent> ent, ref AnchorlessHorrorFormChangedEvent args)
    {
        var visual = EnsureComp<AnchorlessHorrorVisualComponent>(ent);
        visual.HorrorForm = ent.Comp.HorrorForm;
        visual.HorrorSprite = ent.Comp.HorrorSprite;
        visual.HorrorSpriteState = ent.Comp.HorrorSpriteState;
        visual.HorrorScale = ent.Comp.HorrorScale;
        Dirty(ent, visual);
    }
}
