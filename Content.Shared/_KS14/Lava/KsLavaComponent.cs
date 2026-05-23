using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Lava;

/// <summary>
///     Oops, all hardcoded.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(KsLavaSystem))]
public sealed partial class KsLavaComponent : Component
{
    [DataField]
    [AutoNetworkedField]
    public EntityUid? LocalGridUid = null;

    [DataField]
    [AutoNetworkedField]
    public Vector2i LocalTile = Vector2i.Zero;

    [DataField(required: true)]
    public DamageSpecifier Damage = null!;

    [DataField]
    public TimeSpan KnockdownDuration = TimeSpan.Zero;

    [DataField]
    public TimeSpan SinkDuration = TimeSpan.FromSeconds(0.5f);
}
