using Content.Shared._KS14.PopupLocalization;
using Content.Shared.IdentityManagement;
using Content.Shared.Storage.Components;

namespace Content.Shared.Storage.EntitySystems;

public sealed partial class SecretStashSystem
{
    private object GetPopupStashName(Entity<SecretStashComponent> entity)
    {
        return entity.Comp.SecretStashName == null
            ? Identity.Entity(entity, EntityManager)
            : KsPopupMessage.Create(entity.Comp.SecretStashName);
    }
}
