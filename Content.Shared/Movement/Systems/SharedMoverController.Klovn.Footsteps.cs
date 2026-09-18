// KS14: added in this fork
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Robust.Shared.Audio;

namespace Content.Shared.Movement.Systems;

// Landing on a z-level should sound like a step taken on whatever it landed on, which means going through
//      every case that can modify a footstep rather than picking a sound of its own. The chain already exists
//      in TryGetSound; it is only split out here so it can be reached without walking anywhere first.
public abstract partial class SharedMoverController
{
    /// <summary>
    ///     Resolves the footstep sound an entity would make standing exactly where it is.
    /// </summary>
    /// <remarks>
    ///     Every case that can modify a footstep, in the order an ordinary step applies them: a
    ///         <see cref="FootstepModifierComponent"/> on the entity itself, then one on whatever is in its
    ///         shoes slot, then <see cref="GetFootstepSoundEvent"/> and <see cref="FootstepModifierComponent"/>
    ///         on anything anchored to the tile it stands on, then that tile's own footstep or barestep sounds
    ///         depending on whether it is wearing shoes - or, off-grid, the map's modifier.
    ///     <see cref="TryGetSound"/> ends in this and is the only reason it is separate: this half carries no
    ///         step-distance bookkeeping, so a one-off step can be resolved without having moved.
    /// </remarks>
    /// <param name="tileDefinition">The tile being stood on, if the caller already has it.</param>
    /// <returns>Whether this entity makes footsteps at all, and has one to make here.</returns>
    public bool TryGetCurrentFootstepSound(
        Entity<TransformComponent?> entity,
        [NotNullWhen(true)] out SoundSpecifier? sound,
        ContentTileDefinition? tileDefinition = null)
    {
        sound = null;

        if (!XformQuery.Resolve(entity.Owner, ref entity.Comp))
            return false;

        // Also checked before the step-distance bookkeeping in TryGetSound, which is where it has to be for
        //      an ordinary step - repeated here so this is usable on its own.
        if (!_tags.HasTag(entity.Owner, FootstepSoundTag))
            return false;

        if (FootstepModifierQuery.TryComp(entity.Owner, out var moverModifier))
        {
            sound = moverModifier.FootstepSoundCollection;
            return sound != null;
        }

        if (_inventory.TryGetSlotEntity(entity.Owner, "shoes", out var shoes) &&
            FootstepModifierQuery.TryComp(shoes, out var shoesModifier))
        {
            sound = shoesModifier.FootstepSoundCollection;
            return sound != null;
        }

        return TryGetFootstepSound(entity.Owner, entity.Comp, shoes != null, out sound, tileDef: tileDefinition);
    }

    /// <summary>
    ///     Plays a single predicted footstep for an entity where it currently stands, as though it had just
    ///         taken a step there.
    /// </summary>
    /// <param name="volumeModifier">
    ///     Added to the sound's own volume, the way <see cref="InputMoverComponent.WalkingSoundModifier"/> and
    ///         <see cref="InputMoverComponent.SprintingSoundModifier"/> are for an ordinary step.
    /// </param>
    /// <param name="tileDefinition">The tile being stood on, if the caller already has it.</param>
    /// <returns>Whether a footstep was played.</returns>
    public bool TryPlayFootstep(
        Entity<TransformComponent?, MobMoverComponent?> entity,
        float volumeModifier = InputMoverComponent.WalkingSoundModifier,
        ContentTileDefinition? tileDefinition = null)
    {
        // A MobMoverComponent is what the movement path itself requires before it will ever play a step, so
        //      something without one is not a thing that makes footsteps by moving in the first place.
        if (!MobMoverQuery.Resolve(entity.Owner, ref entity.Comp2, logMissing: false) ||
            !TryGetCurrentFootstepSound((entity.Owner, entity.Comp1), out var sound, tileDefinition))
            return false;

        var audioParams = sound.Params
            .WithVolume(sound.Params.Volume + volumeModifier)
            .WithVariation(sound.Params.Variation ?? entity.Comp2.FootstepVariation);

        // As with an ordinary step, a relay target's sound is predicted for whoever is driving it - the mech,
        //      not the pilot, is what makes the noise, but the pilot is the one predicting it.
        RelayTargetQuery.TryComp(entity.Owner, out var relayTargetComponent);

        _audio.PlayPredicted(sound, entity.Owner, relayTargetComponent?.Source ?? entity.Owner, audioParams);
        return true;
    }
}
