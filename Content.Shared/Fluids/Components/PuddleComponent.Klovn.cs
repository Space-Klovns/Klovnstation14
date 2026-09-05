namespace Content.Shared.Fluids.Components;

[AutoGenerateComponentPause]
public sealed partial class PuddleComponent : Component
{
    [DataField(serverOnly: true), AutoPausedField]
    public TimeSpan LastTileEffectUpdate = TimeSpan.Zero;
}
