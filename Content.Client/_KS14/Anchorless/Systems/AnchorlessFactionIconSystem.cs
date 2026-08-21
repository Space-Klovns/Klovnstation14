using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Anchorless.Systems;

/// <summary>Adds the Anchorless faction icon; its prototype limits visibility to the hive.</summary>
public sealed partial class AnchorlessFactionIconSystem : EntitySystem
{
    private static readonly ProtoId<FactionIconPrototype> AnchorlessFactionIcon = "AnchorlessFaction";
    [Dependency] private IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AnchorlessFactionComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<AnchorlessFactionComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_prototype.TryIndex(AnchorlessFactionIcon, out var icon))
            args.StatusIcons.Add(icon);
    }
}
