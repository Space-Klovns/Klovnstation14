using Content.Shared._KS14.GenericSpriteFlick;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Components;

public enum NpcRangedType
{
    Single,
    Spiral,
    DoubleSpiral,
    Shotgun,
    CardinalDirections,
    DiagonalDirections,
    AllDirections,
    RandomAoe,
    Cone,
    Wave,
    TargetedBurst,
    RapidFire
}

/// <summary>
/// Component that defines an NPC attack pattern
/// </summary>
[RegisterComponent]
[EntityCategory("NpcAttackPattern")]
public sealed partial class NpcRangedAttackPatternComponent : Component
{
    [DataField]
    public NpcRangedType AttackType { get; set; } = NpcRangedType.Single;

    [DataField]
    public string Projectile { get; set; } = "BaseNPCProjectile";

    [DataField]
    public int Shots { get; set; } = 1;

    [DataField]
    public float DegreesPerShot { get; set; } = 15f;

    [DataField]
    public float RotationOffset { get; set; } = 0f;

    [DataField]
    public float Spread { get; set; } = 45f;

    [DataField]
    public float Speed { get; set; } = 2;

    [DataField]
    public SoundSpecifier? Sound { get; set; }

    [DataField]
    public KsSpriteFlickData? TelegraphSpriteFlickData { get; set; } = null;

    [DataField]
    public float Cooldown { get; set; } = 2f;

    [DataField]
    public float ShotDelay { get; set; } = 0.1f;

    [DataField]
    public int BurstCount { get; set; } = 1;
}
