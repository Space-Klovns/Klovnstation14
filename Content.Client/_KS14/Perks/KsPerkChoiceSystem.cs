using Content.Shared._KS14.Perks;

namespace Content.Client._KS14.Perks;

/// <inheritdoc/>
public sealed partial class KsPerkChoiceSystem : SharedKsPerkChoiceSystem
{
    [Dependency] private SharedUserInterfaceSystem _userInterfaceSystem = default!;

    protected override void OnOptionsChanged(Entity<KsPerkChoiceComponent> entity)
    {
        if (_userInterfaceSystem.TryGetOpenUi(entity.Owner, KsPerkChoiceUiKey.Key, out var bui))
            bui.Update();
    }
}
