using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.BloodSpray;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Klovnmed.Dismemberment;

/// <summary>
///     All methods that search for bodyparts here assume that
///         all bodyparts will only either be contained in either a Body,
///         or another BodyPart.
/// </summary>
public sealed partial class DismembermentSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private OrganSearchSystem _organSearchSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    //[Dependency] private KsPopupSystem _ksPopupSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedChatSystem _chatSystem = default!;
    [Dependency] private BloodSpraySystem _bloodSpraySystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private INetManager _netManager = default!;

    [Dependency] private EntityQuery<BodyComponent> _bodyQuery = default!;
    private static readonly ProtoId<EmotePrototype> DismemberEmote = "Scream";

    /// <summary>
    ///     TGMC's bone break, as a limb comes off.
    /// </summary>
    private static readonly SoundSpecifier DismemberSound = new SoundCollectionSpecifier("KsBoneBreak", AudioParams.Default.WithVariation(0.05f));

    /// <returns>True if, with the given damage, something can be dismembered from the given entity.</returns>
    public bool CanDismemberByDamage(Entity<BodyComponent?> bodyEntity, FixedPoint2 damageFp2, [NotNullWhen(true)] out float? totalDamage)
    {
        if (!_bodyQuery.Resolve(bodyEntity, ref bodyEntity.Comp, logMissing: false))
        {
            totalDamage = null;
            return false;
        }

        totalDamage = (float)damageFp2;
        return damageFp2 >= bodyEntity.Comp.DismembermentThreshold;
    }

    /// <summary>
    ///     Tries to dismember a random body-part of given type from someone,
    ///         setting its coordinates to those of the victim and throwing it in a random direction.
    ///         Does no throwing logic if <paramref name="throwSpeed"/> is exactly 0f.
    ///
    ///     Supports <see cref="partType"/> having more than one bit set.
    /// </summary>
    /// <returns>Whether anything happened.</returns>
    // wall of parameters
    public bool TryDismemberRandomBodyPartOfType(Entity<BodyComponent?, TransformComponent?> bodyEntity, BodyPartType partType, [NotNullWhen(true)] out EntityUid? partUid, Vector2? direction = null, float throwSpeed = 10f, EntityUid? cause = null, System.Random? predictedRandom = null)
    {
        if (!_bodyQuery.Resolve(bodyEntity, ref bodyEntity.Comp1, logMissing: false) ||
            !EntityManager.TransformQuery.Resolve(bodyEntity, ref bodyEntity.Comp2, logMissing: true) ||
            !_organSearchSystem.TryGetRandomBodyPartOfType((bodyEntity, bodyEntity.Comp1), partType, out predictedRandom, out partUid))
        {
            partUid = null;
            return false;
        }

        predictedRandom ??= KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            (int)_gameTiming.CurTick.Value,
            KsSharedRandomExtensions.GetNetId(bodyEntity.Owner, EntityManager),
            (int)partType
        );

        DismemberPart((bodyEntity.Owner, bodyEntity.Comp2), partUid.Value, direction: direction, throwSpeed: throwSpeed, cause: cause, predictedRandom: predictedRandom);
        return true;
    }

    public void DismemberPart(Entity<TransformComponent?> bodyEntity, EntityUid partUid, Vector2? direction = null, float throwSpeed = 0f, EntityUid? cause = null, System.Random? predictedRandom = null)
    {
        if (!EntityManager.TransformQuery.Resolve(bodyEntity, ref bodyEntity.Comp))
            return;

        var partTransform = Transform(partUid);
        _containerSystem.Remove(partUid, _containerSystem.GetContainer(partTransform.ParentUid, BodyHierarchySystem.ConstContainerId), force: true, destination: bodyEntity.Comp.Coordinates);

        if (throwSpeed != 0f)
        {
            direction ??= (predictedRandom ?? KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
                (int)_gameTiming.CurTick.Value,
                KsSharedRandomExtensions.GetNetId(partUid, EntityManager)
            )).NextUnitVector2();

            _throwingSystem.TryThrow(partUid, direction.Value, baseThrowSpeed: throwSpeed, user: cause, recoil: false);
        }

        // The gore follows the limb; one that falls where it was still bursts, every way at once.
        if (direction is { } sprayDirection && throwSpeed != 0f)
            _bloodSpraySystem.SpawnDismemberParticles(bodyEntity.Owner, sprayDirection, throwSpeed);
        else
            _bloodSpraySystem.SpawnGibParticles(bodyEntity.Owner);

        // From the server to everyone, as the gib sound is: not every dismemberment is predicted by the cause's client
        //      (an explosion's is not), and PlayPredicted would leave that player without it.
        if (_netManager.IsServer)
            _audioSystem.PlayPvs(DismemberSound, bodyEntity.Owner);
    }

    public void DoFixedEmote(EntityUid victimUid)
    {
        if (!_mobStateSystem.IsAlive(victimUid))
            return;

        _chatSystem.TryEmoteWithChat(victimUid, DismemberEmote);
    }

    /// <summary>
    ///     Rolls a predicted random chance to destroy a bodypart.
    /// </summary>
    public void TryRandomlyCrushPart(EntityUid partUid, float chance = 0.5f, EntityUid? victimUid = null, System.Random? predictedRandom = null, bool predicted = true)
    {
        predictedRandom ??= KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            (int)_gameTiming.CurTick.Value,
            KsSharedRandomExtensions.GetNetId(partUid, EntityManager));

        if (!predictedRandom.Prob(chance))
            return;

        PredictedQueueDel(partUid);

        if (victimUid is { })
        {
            // bro
            //     _ksPopupSystem.PopupTargetAndUser(
            //         partUid,
            //         victimUid.Value,
            //         "someones bodypart gets crushed",
            //         "yhour bodypart gets crushed",
            //         type: PopupType.MediumCaution,
            //         predicted: predicted
            //     );
        }
    }
}
