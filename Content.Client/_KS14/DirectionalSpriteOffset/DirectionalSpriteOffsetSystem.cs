namespace Content.Client._KS14.DirectionalSpriteOffsetSystem;

public sealed class DirectionalSpriteOffsetSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
    }

}
