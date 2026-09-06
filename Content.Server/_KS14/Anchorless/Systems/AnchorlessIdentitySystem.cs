using System.Linq;
using Content.Shared._KS14.Anchorless.Components;
using Content.Shared._KS14.Anchorless.Systems;
using Robust.Shared.GameStates;

namespace Content.Server._KS14.Anchorless.Systems;

public sealed class AnchorlessIdentitySystem : SharedAnchorlessIdentitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KsAnchorlessAntagComponent, ComponentGetState>(OnGetState);
    }

    private void OnGetState(Entity<KsAnchorlessAntagComponent> ent, ref ComponentGetState args)
    {
        var identities = ent.Comp.LearnedIdentities.Select(identity => new AnchorlessNetworkedIdentityData
        {
            StoredIdentity = GetNetEntity(identity.StoredIdentity),
            OriginalEntity = GetNetEntity(identity.OriginalEntity),
            OriginalName = identity.OriginalName,
            Starting = identity.Starting,
        }).ToList();

        args.State = new AnchorlessIdentityComponentState(identities, GetNetEntity(ent.Comp.CurrentIdentity), ent.Comp.IdentityCloningSettings,
            ent.Comp.HorrorForm, ent.Comp.HorrorSprite, ent.Comp.HorrorSpriteState);
    }
}
