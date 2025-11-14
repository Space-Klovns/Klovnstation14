namespace Content.Shared._KS14.OverlayStains;

public sealed partial class StainCleanReaction : EntityEffect
{
    public override void Effect(EntityEffectBaseArgs args)
    {
        // KS14: Trollface Emoji ; clean wall-stain-overlays
        args.EntityManager.System<_KS14.OverlayStains.StainSystem>().CleanEntity(args.TargetEntity);
    }
}
