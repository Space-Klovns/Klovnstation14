using Robust.Shared.Audio;

namespace Content.Server._KS14.NPC.Components;

public enum NPCRangedType
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
public sealed partial class NPCRangedComponent : Component
{
    [DataField("attackType")]
    public NPCRangedType AttackType { get; set; } = NPCRangedType.Single;

    [DataField("projectile")]
    public string Projectile { get; set; } = "BaseNPCProjectile";

    [DataField("shots")]
    public int Shots { get; set; } = 1;

    [DataField("degreesPerShot")]
    public float DegreesPerShot { get; set; } = 15f;

    [DataField("rotationOffset")]
    public float RotationOffset { get; set; } = 0f;

    [DataField("spread")]
    public float Spread { get; set; } = 45f;

    [DataField("speed")]
    public float Speed { get; set; } = 2;

    [DataField("sound")]
    public SoundSpecifier? Sound { get; set; }

    [DataField("telegraph")]
    public bool Telegraph { get; set; } = false;

    [DataField("telegraphSpriteState")]
    public string TelegraphSpriteState { get; set; } = "attack";

    [DataField("cooldown")]
    public float Cooldown { get; set; } = 2f;

    [DataField("shotDelay")]
    public float ShotDelay { get; set; } = 0.1f;

    [DataField("burstCount")]
    public int BurstCount { get; set; } = 1;
}
