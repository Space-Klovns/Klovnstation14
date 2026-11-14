namespace Content.Shared.Damage.Components;

public sealed partial class DamageContactsComponent
{
    /// <summary>
    ///     If true, whoever touches this is only damaged while moving: standing still in it is safe, and the first step
    ///         after standing still hurts at once if a damage tick is due.
    /// </summary>
    /// <seealso cref="Content.Shared.Damage.Systems.DamageContactsSystem.KsMovingSpeed"/>
    [DataField]
    public bool OnlyWhileMoving;
}
