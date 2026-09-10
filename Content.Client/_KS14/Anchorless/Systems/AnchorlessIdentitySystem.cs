using System.Linq;
using Content.Shared._KS14.Anchorless.Components;
using Content.Shared._KS14.Anchorless.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Client._KS14.Anchorless.Systems;

public sealed partial class AnchorlessIdentitySystem : SharedAnchorlessIdentitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KsAnchorlessAntagComponent, ComponentHandleState>(OnHandleState);
    }

    private void OnHandleState(Entity<KsAnchorlessAntagComponent> ent, ref ComponentHandleState args)
    {
        if (args.Current is not AnchorlessIdentityComponentState state)
            return;

        ent.Comp.LearnedIdentities = state.LearnedIdentities.Select(identity => new AnchorlessIdentityData
        {
            StoredIdentity = EnsureEntity<KsAnchorlessAntagComponent>(identity.StoredIdentity, ent),
            OriginalEntity = EnsureEntity<KsAnchorlessAntagComponent>(identity.OriginalEntity, ent),
            OriginalName = identity.OriginalName,
            Starting = identity.Starting,
        }).ToList();
        ent.Comp.CurrentIdentity = EnsureEntity<KsAnchorlessAntagComponent>(state.CurrentIdentity, ent);
        ent.Comp.IdentityCloningSettings = state.IdentityCloningSettings;
        _movement.RefreshMovementSpeedModifiers(ent.Owner, CompOrNull<MovementSpeedModifierComponent>(ent));
    }
}
