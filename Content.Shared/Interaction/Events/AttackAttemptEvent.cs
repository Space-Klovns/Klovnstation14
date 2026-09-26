using Content.Shared.Weapons.Melee;

namespace Content.Shared.Interaction.Events
{
    /// <summary>
    ///     Raised Directed at a user to check whether they are allowed to attack a target.
    /// </summary>
    /// <remarks>
    ///     Combat will also check the general interaction blockers, so this event should only be used for combat-specific
    ///     action blocking.
    /// </remarks>
    public sealed class AttackAttemptEvent : CancellableEntityEventArgs
    {
        public EntityUid Uid { get; }
        public EntityUid? Target { get; }

        public Entity<MeleeWeaponComponent>? Weapon { get; }

        /// <summary>
        ///     If this attempt is a disarm as opposed to an actual attack, for things that care about the difference.
        /// </summary>
        public bool Disarm { get; }

        // KS14 start: this is raised both to ask whether an attack could happen - every frame, for some callers - and
        //      when an attack is actually being made
        /// <summary>
        ///     If true, nothing is being attacked: this only asks whether it could be, as the client's melee does every
        ///         frame. Cancel exactly as you would otherwise, but have no side effects - no popups, sounds, or state
        ///         changes - since there is no attack for them to be about.
        /// </summary>
        public bool Pure { get; }
        // KS14 end

        public AttackAttemptEvent(EntityUid uid, EntityUid? target = null, Entity<MeleeWeaponComponent>? weapon = null, bool disarm = false, bool pure = false /* KS14: added param */)
        {
            Uid = uid;
            Target = target;
            Weapon = weapon;
            Disarm = disarm;
            Pure = pure; // KS14
        }
    }

    /// <summary>
    /// Raised directed at an entity to check if they can attack while inside of a container.
    /// </summary>
    public sealed class CanAttackFromContainerEvent : EntityEventArgs
    {
        public EntityUid Uid;
        public EntityUid? Target;
        public bool CanAttack = false;

        public CanAttackFromContainerEvent(EntityUid uid, EntityUid? target = null)
        {
            Uid = uid;
            Target = target;
        }
    }
}
