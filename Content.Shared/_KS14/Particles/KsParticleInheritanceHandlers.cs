using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._KS14.Particles;

/// <summary>
///     A child prototype's <see cref="KsParticlesPrototype.Sprites"/> replace its parent's rather than being appended
///         to them, as BYOND's <c>icon_state</c> list does: steam restates its three sprites and gets three, not six.
///         Leaving the field out still inherits the parent's.
/// </summary>
[TypeSerializer]
public sealed class KsParticleSpritesInheritanceHandler : ITypeInheritanceHandler<List<KsParticleSprite>, SequenceDataNode>
{
    public SequenceDataNode PushInheritance(ISerializationManager serializationManager, SequenceDataNode child, SequenceDataNode parent, IDependencyCollection dependencies, ISerializationContext? context)
    {
        return child;
    }
}

/// <summary>
///     A child prototype's <see cref="KsParticlesPrototype.Gradient"/> replaces its parent's whole, colours included,
///         rather than merging into it, as BYOND's <c>gradient</c> does. Leaving it out still inherits the parent's.
/// </summary>
[TypeSerializer]
public sealed class KsParticleGradientInheritanceHandler : ITypeInheritanceHandler<KsParticleGradient, MappingDataNode>
{
    public MappingDataNode PushInheritance(ISerializationManager serializationManager, MappingDataNode child, MappingDataNode parent, IDependencyCollection dependencies, ISerializationContext? context)
    {
        return child;
    }
}
