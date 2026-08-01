using Content.Shared._KS14.Language;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Language;

/// <summary>
///     Grants intrinsic language knowledge on spawn, for character-creation traits. Merges
///     additively; stacked AddComponentSpecial registries would overwrite each other's grant.
/// </summary>
public sealed partial class KsLanguageAddSpecial : JobSpecial
{
    [DataField]
    public List<ProtoId<KsLanguagePrototype>> Speaks = new();

    [DataField]
    public List<ProtoId<KsLanguagePrototype>> Understands = new();

    public override void AfterEquip(EntityUid mob)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        entMan.System<KsLanguageSystem>().AddKnowledge(mob, Speaks, Understands);
    }
}
