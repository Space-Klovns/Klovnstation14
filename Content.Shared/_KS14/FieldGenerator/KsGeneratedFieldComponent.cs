namespace Content.Shared._KS14.FieldGenerator;

[RegisterComponent]
[Access(typeof(KsFieldGeneratorSystem), typeof(KsGeneratedFieldSystem))]
public sealed partial class KsGeneratedFieldComponent : Component
{
    /// <summary>
    ///     The UID of the generators that own this.
    ///         Equal to <see cref="EntityUid.Invalid"/> if the
    ///         generator is in the process of being shut down or something.
    ///
    ///     Only processed server-side.
    /// </summary>
    [ViewVariables, DataField(serverOnly: true)]
    public List<EntityUid> GeneratorUids = [];
}
