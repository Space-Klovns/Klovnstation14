using Content.Shared._KS14.Speczones;

namespace Content.Client._KS14.Speczones;

/// <inheritdoc/>
public sealed partial class SpeczoneSystem : SharedSpeczoneSystem
{
    protected override bool HasSpeczoneComponent(EntityUid uid) => HasComp<SpeczoneComponent>(uid);
}
