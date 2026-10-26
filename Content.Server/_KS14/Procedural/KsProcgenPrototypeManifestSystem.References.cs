using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestSystem
{
    private sealed class ReferenceWork(KsProcgenPrototypeManifestLimits limits)
    {
        public readonly HashSet<(KsProcgenPrototypeBodyKind Kind, string Id)> Seen = new();
        public readonly SortedSet<(KsProcgenPrototypeBodyKind Kind, string Id)> Pending = new(
            Comparer<(KsProcgenPrototypeBodyKind Kind, string Id)>.Create((first, second) =>
            {
                var kind = first.Kind.CompareTo(second.Kind);
                return kind != 0 ? kind : StringComparer.Ordinal.Compare(first.Id, second.Id);
            }));
        public KsProcgenIssue? Issue;
        public readonly List<KsProcgenPrototypeAbstractTemplate> AbstractTemplates = new();
        public readonly HashSet<KsProcgenPrototypeReference> DependencySeen = new();
        public readonly SortedSet<KsProcgenPrototypeReference> DependencyPending = new(
            Comparer<KsProcgenPrototypeReference>.Create((first, second) =>
            {
                var kind = StringComparer.Ordinal.Compare(first.Kind, second.Kind);
                return kind != 0 ? kind : StringComparer.Ordinal.Compare(first.PrototypeId, second.PrototypeId);
            }));
        private int _occurrences;
        public int Occurrences => _occurrences;
        public void Add(KsProcgenPrototypeBodyKind kind, string id)
        {
            if (!Occurrence(id)) return;
            if (!Seen.Add((kind, id))) return;
            if (Seen.Count + DependencySeen.Count > limits.MaximumDefinitions)
            { Issue = new("PrototypeManifestDefinitionBudget", "Root and dependency definitions exceed the shared allowance."); return; }
            Pending.Add((kind, id));
        }

        public void AddDependency(string kind, string id)
        {
            if (!Occurrence(id)) return;
            if (string.IsNullOrWhiteSpace(kind) || kind.Length > 256)
            { Issue = new("InvalidPrototypeManifestReference", "Dependency kinds require bounded registered names."); return; }
            var reference = new KsProcgenPrototypeReference(kind, id);
            if (!DependencySeen.Add(reference)) return;
            if (Seen.Count + DependencySeen.Count > limits.MaximumDefinitions)
            { Issue = new("PrototypeManifestDefinitionBudget", "Root and dependency definitions exceed the shared allowance."); return; }
            DependencyPending.Add(reference);
        }

        private bool Occurrence(string id)
        {
            if (Issue != null) return false;
            if (_occurrences++ >= limits.MaximumReferences)
            { Issue = new("PrototypeManifestReferenceBudget", "Root and typed dependency occurrences exceed the shared allowance."); return false; }
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256)
            { Issue = new("InvalidPrototypeManifestReference", "References require bounded nonempty prototype IDs."); return false; }
            return true;
        }
    }

    private sealed class ComponentEntityReferenceWriter<T> : ITypeWriter<EntProtoId<T>> where T : IComponent, new()
    {
        public ComponentEntityReferenceWriter() { }
        public DataNode Write(ISerializationManager serializationManager, EntProtoId<T> value,
            IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
        {
            (context as PrototypeContext)?.ObserveEntityReference(value.Id);
            return new ValueDataNode(value.Id);
        }
    }

    private sealed class TypedPrototypeReferenceWriter<T> : ITypeWriter<ProtoId<T>> where T : class, IPrototype
    {
        public TypedPrototypeReferenceWriter() { }
        public DataNode Write(ISerializationManager serializationManager, ProtoId<T> value,
            IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
        {
            (context as PrototypeContext)?.ObservePrototypeReference(typeof(T), value.Id);
            return new ValueDataNode(value.Id);
        }
    }
}
