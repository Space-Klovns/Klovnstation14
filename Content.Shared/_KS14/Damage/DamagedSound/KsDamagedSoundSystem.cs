using Content.Shared._KS14.Damage.Events;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Damage.DamagedSound;

/// <summary>
///     Plays <see cref="KsDamagedSoundComponent.Sound"/> when its entity is hit or damaged, or hurts something by contact.
/// </summary>
/// <remarks>
///     Each trigger is predicted only where the client really runs it, and only for the player who caused it:
///     <list type="bullet">
///         <item>
///             A melee hit (<see cref="AttackedEvent"/>, raised by the attacker's client as it predicts the swing):
///                 the attacker hears it from their own client, everyone else from the server.
///         </item>
///         <item>
///             Contact damage (<see cref="KsDamagedByContactEvent"/>), whose ticks the client runs in step with the
///                 server for its own mob: the mob's player hears it from their client, everyone else from the server.
///         </item>
///         <item>
///             Any other damage (<see cref="DamageChangedEvent"/>): projectiles, explosions, fire. The server plays
///                 it for everyone. A projectile's hit lands on the shooter's client in a kept copy, often on a
///                 re-predicted tick where the engine plays no sound at all, and sometimes on a different target than
///                 the server's copy, so it isn't predicted: excluding the shooter would often leave them hearing
///                 nothing. A melee hit's own damage follows its <see cref="AttackedEvent"/> within the cooldown, so
///                 it doesn't play twice.
///         </item>
///     </list>
/// </remarks>
public sealed partial class KsDamagedSoundSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private EntityQuery<KsDamagedSoundComponent> _damagedSoundQuery = default!;

    // By-value class event: no Entity<T> handler for it (RA0054).
    [SubscribeLocalEvent]
    private void OnAttacked(EntityUid uid, KsDamagedSoundComponent component, AttackedEvent args)
    {
        if (_netManager.IsClient && !IsPredictingFor(args.User))
            return;

        TryPlay((uid, component), args.User);
    }

    // By-value class event: no Entity<T> handler for it (RA0054).
    [SubscribeLocalEvent]
    private void OnDamageChanged(EntityUid uid, KsDamagedSoundComponent component, DamageChangedEvent args)
    {
        // The server plays it for everyone; see the class remarks.
        if (_netManager.IsClient || !args.DamageIncreased)
            return;

        TryPlay((uid, component), predictingUser: null);
    }

    [SubscribeLocalEvent]
    private void OnDamagedByContact(Entity<DamagedByContactComponent> entity, ref KsDamagedByContactEvent args)
    {
        if (_netManager.IsClient && !IsPredictingFor(entity.Owner))
            return;

        // The source that did the damage, not everything touched that could have: astride two pieces of wire, one of
        //     them cuts, and one rattles.
        if (_damagedSoundQuery.TryComp(args.SourceUid, out var damagedSoundComponent) &&
            damagedSoundComponent.PlayOnContactDamage)
            TryPlay((args.SourceUid, damagedSoundComponent), entity.Owner);
    }

    /// <summary>
    ///     Whether this client is predicting, for the first time, something <paramref name="userUid"/> does. Anyone
    ///         else's, or a re-prediction, is heard from the server (or was already played).
    /// </summary>
    private bool IsPredictingFor(EntityUid userUid)
    {
        return _gameTiming.IsFirstTimePredicted && userUid == _playerManager.LocalEntity;
    }

    /// <param name="predictingUser">
    ///     The player whose client plays this itself, whom the server's sound leaves out. Null when no client predicts it.
    /// </param>
    private void TryPlay(Entity<KsDamagedSoundComponent> entity, EntityUid? predictingUser)
    {
        var currentTime = _gameTiming.CurTime;
        if (currentTime < entity.Comp.NextSound)
            return;

        entity.Comp.NextSound = currentTime + entity.Comp.Cooldown;

        // At its coordinates rather than on it: the hit that plays it can be the one that destroys it.
        _audioSystem.PlayPredicted(entity.Comp.Sound, Transform(entity.Owner).Coordinates, predictingUser);
    }
}
