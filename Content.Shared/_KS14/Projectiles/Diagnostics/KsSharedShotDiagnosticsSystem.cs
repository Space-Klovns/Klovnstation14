using System.Globalization;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Standing;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Projectiles.Diagnostics;

/// <summary>
///     What decides whether a shot hits an entity, as this side has it, for the <c>ks_shotdiag</c> command: a client
///         prints its own beside the server's, and marks where they differ. A shot the server lands and the client's
///         copy of flies through is the two sides disagreeing on one of these.
/// </summary>
public abstract partial class KsSharedShotDiagnosticsSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingStateSystem = default!;

    /// <summary>
    ///     What this side has no value for: the other side's alone.
    /// </summary>
    public const string NoValue = "-";

    /// <summary>
    ///     Keys whose values are expected to differ a little between the sides, and are not marked when they do.
    /// </summary>
    public static readonly string[] ApproximateKeys = ["position"];

    /// <summary>
    ///     Adds what both sides have about <paramref name="uid"/> to <paramref name="keys"/> and <paramref name="values"/>,
    ///         in the same order on each.
    /// </summary>
    protected void CollectShared(EntityUid uid, List<string> keys, List<string> values)
    {
        void Add(string key, object? value)
        {
            keys.Add(key);
            values.Add(value?.ToString() ?? NoValue);
        }

        Add("prototype", MetaData(uid).EntityPrototype?.ID);
        Add("mob state", TryComp<MobStateComponent>(uid, out var mobStateComponent) ? mobStateComponent.CurrentState : NoValue);
        Add("down", _standingStateSystem.IsDown(uid));
        Add("needs to be clicked on", TryComp<RequireProjectileTargetComponent>(uid, out var requireTargetComponent) ? requireTargetComponent.Active : NoValue);
        Add("damage", _damageableSystem.GetTotalDamage(uid));

        if (TryComp<PhysicsComponent>(uid, out var physicsComponent))
        {
            Add("body type", physicsComponent.BodyType);
            Add("can collide", physicsComponent.CanCollide);
            Add("awake", physicsComponent.Awake);
            Add("contacts", physicsComponent.ContactCount);
        }
        else
        {
            Add("body type", NoValue);
            Add("can collide", NoValue);
            Add("awake", NoValue);
            Add("contacts", NoValue);
        }

        Add("collision wake", TryComp<CollisionWakeComponent>(uid, out var collisionWakeComponent) ? collisionWakeComponent.Enabled : NoValue);

        var hardFixture = NoValue;
        if (TryComp<FixturesComponent>(uid, out var fixturesComponent))
        {
            foreach (var (fixtureId, fixture) in fixturesComponent.Fixtures)
            {
                if (!fixture.Hard)
                    continue;

                hardFixture = $"{fixtureId} layer {fixture.CollisionLayer} mask {fixture.CollisionMask}";
                break;
            }
        }

        Add("hard fixture", hardFixture);

        var transformComponent = Transform(uid);
        var position = _transformSystem.GetWorldPosition(transformComponent);
        Add("position", $"{position.X.ToString("F2", CultureInfo.InvariantCulture)}, {position.Y.ToString("F2", CultureInfo.InvariantCulture)}");
        Add("grid", GetNetEntity(transformComponent.GridUid));
        Add("parent", GetNetEntity(transformComponent.ParentUid));
        Add("paused", Paused(uid));
    }
}

/// <summary>
///     The server tells a client, whose admin ran <c>ks_shotdiag</c>, to gather its half: about <paramref name="Target"/>,
///         or whoever its cursor is on.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsShotDiagnosticsStartEvent(NetEntity? target) : EntityEventArgs
{
    public readonly NetEntity? Target = target;
}

/// <summary>
///     A client asks what the server has about <paramref name="Target"/>. Answered only for an admin with the debug flag.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsShotDiagnosticsRequestEvent(NetEntity target) : EntityEventArgs
{
    public readonly NetEntity Target = target;
}

/// <summary>
///     What the server has about <see cref="Target"/>, or that it will not say.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsShotDiagnosticsResponseEvent(NetEntity target, bool refused, string[] keys, string[] values) : EntityEventArgs
{
    public readonly NetEntity Target = target;

    /// <summary>
    ///     Whether the server did not answer: the asker is no admin with the debug flag, or the entity is not there.
    /// </summary>
    public readonly bool Refused = refused;

    public readonly string[] Keys = keys;
    public readonly string[] Values = values;
}
